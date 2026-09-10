using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataverseMasterDataMigrator.Core.Abstractions;
using DataverseMasterDataMigrator.Core.Models;
using Umayor.TestDataSeeder.Core.SubjectGraph;
using Xunit;

namespace Umayor.TestDataSeeder.Tests.SubjectGraph
{
    /// <summary>
    /// Prueba, fila por fila, cada una de las 28 reglas de docs/SUBJECT_RELATIONSHIP_MAP.md
    /// contra <see cref="SubjectRelationshipMap"/> — no solo inspeccionando el <see cref="RecordFilter"/>
    /// resultante, sino ejecutándolo de verdad contra <see cref="FakeSourceRecordService"/>.
    ///
    /// Convención de nombres: "RowNN_Tabla_..." donde NN es el número de fila del documento.
    /// </summary>
    public class SubjectRelationshipMapTests
    {
        // --- Helpers ---------------------------------------------------------------------------

        /// <summary>Contexto con TODOS los campos opcionales seteados a GUIDs distintos entre sí
        /// (y distintos de ContactId) — así un test que confunda un campo por otro (p. ej. usar
        /// WitCaso donde debería usar WitEventoOrigen) se detecta de inmediato.</summary>
        private static SubjectContext FullContext(Guid? contactId = null) => new SubjectContext
        {
            ContactId = contactId ?? Guid.NewGuid(),
            Rut = "11.111.111-1",
            OriginatingLeadId = Guid.NewGuid(),
            WitCaso = Guid.NewGuid(),
            WitEventoOrigen = Guid.NewGuid(),
            WitColegio = Guid.NewGuid(),
            WitTramo = Guid.NewGuid(),
            WitIngresoBrutoFamiliar = Guid.NewGuid()
        };

        /// <summary>Contexto solo con ContactId (y opcionalmente Rut) — el resto de los campos
        /// opcionales quedan en null, como un contact real sin esos lookups cargados.</summary>
        private static SubjectContext MinimalContext(Guid? contactId = null, string rut = null) => new SubjectContext
        {
            ContactId = contactId ?? Guid.NewGuid(),
            Rut = rut
        };

        private static SubjectTableRule Rule(string logicalName)
            => SubjectRelationshipMap.Rules.Single(r => string.Equals(r.LogicalName, logicalName, StringComparison.OrdinalIgnoreCase));

        private static Dictionary<string, List<DataRecord>> Data(params (string LogicalName, DataRecord[] Records)[] tables)
        {
            var dict = new Dictionary<string, List<DataRecord>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (logicalName, records) in tables)
                dict[logicalName] = new List<DataRecord>(records);
            return dict;
        }

        private static Dictionary<string, List<DataRecord>> Data(string logicalName, params DataRecord[] records)
            => Data((logicalName, records));

        /// <summary>Arma el filtro de la regla y lo ejecuta de verdad contra
        /// <see cref="FakeSourceRecordService"/>, devolviendo los <c>.Id</c> de los registros de
        /// <paramref name="rule"/>.LogicalName que matchearon.</summary>
        private static async Task<List<Guid>> MatchingIdsAsync(
            SubjectTableRule rule, SubjectContext ctx, Dictionary<string, List<DataRecord>> data)
        {
            IDataverseRecordService service = new FakeSourceRecordService(data);
            var filter = await rule.BuildFilterAsync(ctx, service, CancellationToken.None).ConfigureAwait(false);
            var page = await service
                .RetrieveFilteredPageAsync(rule.LogicalName, Array.Empty<string>(), filter, null, 1000, CancellationToken.None)
                .ConfigureAwait(false);
            return page.Records.Select(r => r.Id).ToList();
        }

        /// <summary>Un registro "en blanco" de <paramref name="logicalName"/>: sin ningún
        /// atributo seteado. Sirve para el requisito (c): si una regla generara por error una
        /// condición "atributo = null" para un campo de <see cref="SubjectContext"/> que vino
        /// null, este registro (que tampoco tiene el atributo seteado) matchearía por error.</summary>
        private static DataRecord Blank(string logicalName) => new DataRecord(logicalName, Guid.NewGuid());

        private static void SetLookup(DataRecord record, string attribute, Guid targetId, string targetLogicalName = "target")
            => record.Attributes[attribute] = new DataReference(targetLogicalName, targetId);

        private static DataRecord SelfKeyed(string logicalName, string primaryAttribute, Guid? id = null)
        {
            var recordId = id ?? Guid.NewGuid();
            var record = new DataRecord(logicalName, recordId);
            record.Attributes[primaryAttribute] = recordId;
            return record;
        }

        // --- Fila 1: contact ---------------------------------------------------------------

        [Fact]
        public async Task Row01_Contact_ContactId_MatchesSelf_ExcludesOtherContact()
        {
            var ctx = FullContext();
            var self = SelfKeyed("contact", "contactid", ctx.ContactId);
            var other = SelfKeyed("contact", "contactid");

            var ids = await MatchingIdsAsync(Rule("contact"), ctx, Data("contact", self, other));

            Assert.Contains(ctx.ContactId, ids);
            Assert.DoesNotContain(other.Id, ids);
        }

        // --- Fila 2: lead --------------------------------------------------------------------

        [Fact]
        public async Task Row02_Lead_MatchesCustomerId_ParentContactId_AndOriginatingLeadId_ExcludesUnrelated()
        {
            var ctx = FullContext();

            var byCustomer = new DataRecord("lead", Guid.NewGuid());
            SetLookup(byCustomer, "customerid", ctx.ContactId, "contact");

            var byParent = new DataRecord("lead", Guid.NewGuid());
            SetLookup(byParent, "parentcontactid", ctx.ContactId, "contact");

            var byOriginating = SelfKeyed("lead", "leadid", ctx.OriginatingLeadId.Value);

            var unrelated = new DataRecord("lead", Guid.NewGuid());
            SetLookup(unrelated, "customerid", Guid.NewGuid(), "contact");

            var ids = await MatchingIdsAsync(Rule("lead"), ctx, Data("lead", byCustomer, byParent, byOriginating, unrelated));

            Assert.Contains(byCustomer.Id, ids);
            Assert.Contains(byParent.Id, ids);
            Assert.Contains(byOriginating.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        [Fact]
        public async Task Row02_Lead_OriginatingLeadIdNull_DoesNotMatchBlankLead()
        {
            var ctx = MinimalContext();
            var blank = Blank("lead");

            var ids = await MatchingIdsAsync(Rule("lead"), ctx, Data("lead", blank));

            Assert.DoesNotContain(blank.Id, ids);
        }

        // --- Fila 3: incident ------------------------------------------------------------------

        [Fact]
        public async Task Row03_Incident_MatchesAllFourContactLookups_AndWitCaso_ExcludesUnrelated()
        {
            var ctx = FullContext();

            var byCustomer = new DataRecord("incident", Guid.NewGuid());
            SetLookup(byCustomer, "customerid", ctx.ContactId, "contact");
            var byPrimary = new DataRecord("incident", Guid.NewGuid());
            SetLookup(byPrimary, "primarycontactid", ctx.ContactId, "contact");
            var byResponsible = new DataRecord("incident", Guid.NewGuid());
            SetLookup(byResponsible, "responsiblecontactid", ctx.ContactId, "contact");
            var byPartner = new DataRecord("incident", Guid.NewGuid());
            SetLookup(byPartner, "msa_partnercontactid", ctx.ContactId, "contact");
            var byWitCaso = SelfKeyed("incident", "incidentid", ctx.WitCaso.Value);
            var unrelated = new DataRecord("incident", Guid.NewGuid());
            SetLookup(unrelated, "customerid", Guid.NewGuid(), "contact");

            var ids = await MatchingIdsAsync(
                Rule("incident"), ctx,
                Data("incident", byCustomer, byPrimary, byResponsible, byPartner, byWitCaso, unrelated));

            Assert.Contains(byCustomer.Id, ids);
            Assert.Contains(byPrimary.Id, ids);
            Assert.Contains(byResponsible.Id, ids);
            Assert.Contains(byPartner.Id, ids);
            Assert.Contains(byWitCaso.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        [Fact]
        public async Task Row03_Incident_WitCasoNull_DoesNotMatchBlankIncident()
        {
            var ctx = MinimalContext();
            var blank = Blank("incident");

            var ids = await MatchingIdsAsync(Rule("incident"), ctx, Data("incident", blank));

            Assert.DoesNotContain(blank.Id, ids);
        }

        // --- Fila 4: phonecall (regardingobjectid OR wit_rut) -----------------------------------

        [Fact]
        public async Task Row04_PhoneCall_MatchesByRegardingObjectId_AndByWitRut_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var byRegarding = new DataRecord("phonecall", Guid.NewGuid());
            SetLookup(byRegarding, "regardingobjectid", ctx.ContactId, "contact");
            var byRut = new DataRecord("phonecall", Guid.NewGuid());
            byRut.Attributes["wit_rut"] = ctx.Rut;
            var unrelated = new DataRecord("phonecall", Guid.NewGuid());
            unrelated.Attributes["wit_rut"] = "99.999.999-9";

            var ids = await MatchingIdsAsync(Rule("phonecall"), ctx, Data("phonecall", byRegarding, byRut, unrelated));

            Assert.Contains(byRegarding.Id, ids);
            Assert.Contains(byRut.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        [Fact]
        public async Task Row04_PhoneCall_RutNull_DoesNotMatchBlankRecord()
        {
            var ctx = MinimalContext(); // Rut null
            var blank = Blank("phonecall");

            var ids = await MatchingIdsAsync(Rule("phonecall"), ctx, Data("phonecall", blank));

            Assert.DoesNotContain(blank.Id, ids);
        }

        // --- Fila 5: email -----------------------------------------------------------------------

        [Fact]
        public async Task Row05_Email_MatchesRegardingObjectId_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var matching = new DataRecord("email", Guid.NewGuid());
            SetLookup(matching, "regardingobjectid", ctx.ContactId, "contact");
            var unrelated = new DataRecord("email", Guid.NewGuid());
            SetLookup(unrelated, "regardingobjectid", Guid.NewGuid(), "contact");

            var ids = await MatchingIdsAsync(Rule("email"), ctx, Data("email", matching, unrelated));

            Assert.Contains(matching.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        // --- Fila 6: wit_actividadchat (regardingobjectid OR wit_rut) ---------------------------

        [Fact]
        public async Task Row06_ActividadChat_MatchesByRegardingObjectId_AndByWitRut_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var byRegarding = new DataRecord("wit_actividadchat", Guid.NewGuid());
            SetLookup(byRegarding, "regardingobjectid", ctx.ContactId, "contact");
            var byRut = new DataRecord("wit_actividadchat", Guid.NewGuid());
            byRut.Attributes["wit_rut"] = ctx.Rut;
            var unrelated = new DataRecord("wit_actividadchat", Guid.NewGuid());

            var ids = await MatchingIdsAsync(Rule("wit_actividadchat"), ctx, Data("wit_actividadchat", byRegarding, byRut, unrelated));

            Assert.Contains(byRegarding.Id, ids);
            Assert.Contains(byRut.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        [Fact]
        public async Task Row06_ActividadChat_RutNull_DoesNotMatchBlankRecord()
        {
            var ctx = MinimalContext();
            var blank = Blank("wit_actividadchat");

            var ids = await MatchingIdsAsync(Rule("wit_actividadchat"), ctx, Data("wit_actividadchat", blank));

            Assert.DoesNotContain(blank.Id, ids);
        }

        // --- Fila 7: wit_visitaweb ---------------------------------------------------------------

        [Fact]
        public async Task Row07_VisitaWeb_MatchesRegardingObjectId_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var matching = new DataRecord("wit_visitaweb", Guid.NewGuid());
            SetLookup(matching, "regardingobjectid", ctx.ContactId, "contact");
            var unrelated = Blank("wit_visitaweb");

            var ids = await MatchingIdsAsync(Rule("wit_visitaweb"), ctx, Data("wit_visitaweb", matching, unrelated));

            Assert.Contains(matching.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        // --- Fila 8: wit_visitapresencial ---------------------------------------------------------

        [Fact]
        public async Task Row08_VisitaPresencial_MatchesRegardingObjectId_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var matching = new DataRecord("wit_visitapresencial", Guid.NewGuid());
            SetLookup(matching, "regardingobjectid", ctx.ContactId, "contact");
            var unrelated = Blank("wit_visitapresencial");

            var ids = await MatchingIdsAsync(Rule("wit_visitapresencial"), ctx, Data("wit_visitapresencial", matching, unrelated));

            Assert.Contains(matching.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        // --- Fila 9: wit_evento (regardingobjectid OR activityid=WitEventoOrigen) ----------------

        [Fact]
        public async Task Row09_Evento_MatchesRegardingObjectId_AndWitEventoOrigen_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var byRegarding = new DataRecord("wit_evento", Guid.NewGuid());
            SetLookup(byRegarding, "regardingobjectid", ctx.ContactId, "contact");
            var byOrigen = SelfKeyed("wit_evento", "activityid", ctx.WitEventoOrigen.Value);
            var unrelated = Blank("wit_evento");

            var ids = await MatchingIdsAsync(Rule("wit_evento"), ctx, Data("wit_evento", byRegarding, byOrigen, unrelated));

            Assert.Contains(byRegarding.Id, ids);
            Assert.Contains(byOrigen.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        [Fact]
        public async Task Row09_Evento_WitEventoOrigenNull_DoesNotMatchBlankRecord()
        {
            var ctx = MinimalContext();
            var blank = Blank("wit_evento");

            var ids = await MatchingIdsAsync(Rule("wit_evento"), ctx, Data("wit_evento", blank));

            Assert.DoesNotContain(blank.Id, ids);
        }

        // --- Fila 10: wit_procesodepostulacion ----------------------------------------------------

        [Fact]
        public async Task Row10_ProcesoDePostulacion_MatchesContactoAndReferente_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var byContacto = new DataRecord("wit_procesodepostulacion", Guid.NewGuid());
            SetLookup(byContacto, "wit_contacto", ctx.ContactId, "contact");
            var byReferente = new DataRecord("wit_procesodepostulacion", Guid.NewGuid());
            SetLookup(byReferente, "wit_referente", ctx.ContactId, "contact");
            var unrelated = Blank("wit_procesodepostulacion");

            var ids = await MatchingIdsAsync(Rule("wit_procesodepostulacion"), ctx, Data("wit_procesodepostulacion", byContacto, byReferente, unrelated));

            Assert.Contains(byContacto.Id, ids);
            Assert.Contains(byReferente.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        // --- Fila 11: wit_solicituddeadmisiondirecta (wit_postulante OR wit_rut) ------------------

        [Fact]
        public async Task Row11_SolicitudDeAdmisionDirecta_MatchesByPostulante_AndByWitRut_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var byPostulante = new DataRecord("wit_solicituddeadmisiondirecta", Guid.NewGuid());
            SetLookup(byPostulante, "wit_postulante", ctx.ContactId, "contact");
            var byRut = new DataRecord("wit_solicituddeadmisiondirecta", Guid.NewGuid());
            byRut.Attributes["wit_rut"] = ctx.Rut;
            var unrelated = Blank("wit_solicituddeadmisiondirecta");

            var ids = await MatchingIdsAsync(Rule("wit_solicituddeadmisiondirecta"), ctx, Data("wit_solicituddeadmisiondirecta", byPostulante, byRut, unrelated));

            Assert.Contains(byPostulante.Id, ids);
            Assert.Contains(byRut.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        [Fact]
        public async Task Row11_SolicitudDeAdmisionDirecta_RutNull_DoesNotMatchBlankRecord()
        {
            var ctx = MinimalContext();
            var blank = Blank("wit_solicituddeadmisiondirecta");

            var ids = await MatchingIdsAsync(Rule("wit_solicituddeadmisiondirecta"), ctx, Data("wit_solicituddeadmisiondirecta", blank));

            Assert.DoesNotContain(blank.Id, ids);
        }

        // --- Fila 12: wit_historicocontactos --------------------------------------------------------

        [Fact]
        public async Task Row12_HistoricoContactos_MatchesContactAndContactoRelacionado_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var byContact = new DataRecord("wit_historicocontactos", Guid.NewGuid());
            SetLookup(byContact, "wit_contact", ctx.ContactId, "contact");
            var byRelacionado = new DataRecord("wit_historicocontactos", Guid.NewGuid());
            SetLookup(byRelacionado, "wit_contactorelacionado", ctx.ContactId, "contact");
            var unrelated = Blank("wit_historicocontactos");

            var ids = await MatchingIdsAsync(Rule("wit_historicocontactos"), ctx, Data("wit_historicocontactos", byContact, byRelacionado, unrelated));

            Assert.Contains(byContact.Id, ids);
            Assert.Contains(byRelacionado.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        // --- Fila 13: wit_historicodatosdecontactabilidad (wit_contacto OR wit_rut) ----------------

        [Fact]
        public async Task Row13_HistoricoDatosDeContactabilidad_MatchesByContacto_AndByWitRut_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var byContacto = new DataRecord("wit_historicodatosdecontactabilidad", Guid.NewGuid());
            SetLookup(byContacto, "wit_contacto", ctx.ContactId, "contact");
            var byRut = new DataRecord("wit_historicodatosdecontactabilidad", Guid.NewGuid());
            byRut.Attributes["wit_rut"] = ctx.Rut;
            var unrelated = Blank("wit_historicodatosdecontactabilidad");

            var ids = await MatchingIdsAsync(Rule("wit_historicodatosdecontactabilidad"), ctx, Data("wit_historicodatosdecontactabilidad", byContacto, byRut, unrelated));

            Assert.Contains(byContacto.Id, ids);
            Assert.Contains(byRut.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        [Fact]
        public async Task Row13_HistoricoDatosDeContactabilidad_RutNull_DoesNotMatchBlankRecord()
        {
            var ctx = MinimalContext();
            var blank = Blank("wit_historicodatosdecontactabilidad");

            var ids = await MatchingIdsAsync(Rule("wit_historicodatosdecontactabilidad"), ctx, Data("wit_historicodatosdecontactabilidad", blank));

            Assert.DoesNotContain(blank.Id, ids);
        }

        // --- Fila 14: wit_contactodelsegmentocomercial (wit_contacto OR wit_rut) -------------------

        [Fact]
        public async Task Row14_ContactoDelSegmentoComercial_MatchesByContacto_AndByWitRut_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var byContacto = new DataRecord("wit_contactodelsegmentocomercial", Guid.NewGuid());
            SetLookup(byContacto, "wit_contacto", ctx.ContactId, "contact");
            var byRut = new DataRecord("wit_contactodelsegmentocomercial", Guid.NewGuid());
            byRut.Attributes["wit_rut"] = ctx.Rut;
            var unrelated = Blank("wit_contactodelsegmentocomercial");

            var ids = await MatchingIdsAsync(Rule("wit_contactodelsegmentocomercial"), ctx, Data("wit_contactodelsegmentocomercial", byContacto, byRut, unrelated));

            Assert.Contains(byContacto.Id, ids);
            Assert.Contains(byRut.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        [Fact]
        public async Task Row14_ContactoDelSegmentoComercial_RutNull_DoesNotMatchBlankRecord()
        {
            var ctx = MinimalContext();
            var blank = Blank("wit_contactodelsegmentocomercial");

            var ids = await MatchingIdsAsync(Rule("wit_contactodelsegmentocomercial"), ctx, Data("wit_contactodelsegmentocomercial", blank));

            Assert.DoesNotContain(blank.Id, ids);
        }

        // --- Fila 15: wit_colegio ------------------------------------------------------------------

        [Fact]
        public async Task Row15_Colegio_MatchesColegioId_AndAllFourRoles_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var byColegioId = SelfKeyed("wit_colegio", "wit_colegioid", ctx.WitColegio.Value);
            var byCoordinador = new DataRecord("wit_colegio", Guid.NewGuid());
            SetLookup(byCoordinador, "wit_coordinador", ctx.ContactId, "contact");
            var byDirector = new DataRecord("wit_colegio", Guid.NewGuid());
            SetLookup(byDirector, "wit_director", ctx.ContactId, "contact");
            var byEncargado = new DataRecord("wit_colegio", Guid.NewGuid());
            SetLookup(byEncargado, "wit_encargado", ctx.ContactId, "contact");
            var byOrientador = new DataRecord("wit_colegio", Guid.NewGuid());
            SetLookup(byOrientador, "wit_orientador", ctx.ContactId, "contact");
            var unrelated = Blank("wit_colegio");

            var ids = await MatchingIdsAsync(
                Rule("wit_colegio"), ctx,
                Data("wit_colegio", byColegioId, byCoordinador, byDirector, byEncargado, byOrientador, unrelated));

            Assert.Contains(byColegioId.Id, ids);
            Assert.Contains(byCoordinador.Id, ids);
            Assert.Contains(byDirector.Id, ids);
            Assert.Contains(byEncargado.Id, ids);
            Assert.Contains(byOrientador.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        [Fact]
        public async Task Row15_Colegio_WitColegioNull_DoesNotMatchBlankRecord()
        {
            var ctx = MinimalContext();
            var blank = Blank("wit_colegio");

            var ids = await MatchingIdsAsync(Rule("wit_colegio"), ctx, Data("wit_colegio", blank));

            Assert.DoesNotContain(blank.Id, ids);
        }

        // --- Fila 16: wit_ingresofamiliarbruto ------------------------------------------------------

        [Fact]
        public async Task Row16_IngresoFamiliarBruto_MatchesWitTramo_AndWitIngresoBrutoFamiliar_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var byTramo = SelfKeyed("wit_ingresofamiliarbruto", "wit_ingresofamiliarbrutoid", ctx.WitTramo.Value);
            var byIngreso = SelfKeyed("wit_ingresofamiliarbruto", "wit_ingresofamiliarbrutoid", ctx.WitIngresoBrutoFamiliar.Value);
            var unrelated = SelfKeyed("wit_ingresofamiliarbruto", "wit_ingresofamiliarbrutoid");

            var ids = await MatchingIdsAsync(Rule("wit_ingresofamiliarbruto"), ctx, Data("wit_ingresofamiliarbruto", byTramo, byIngreso, unrelated));

            Assert.Contains(byTramo.Id, ids);
            Assert.Contains(byIngreso.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        [Fact]
        public async Task Row16_IngresoFamiliarBruto_BothContextFieldsNull_ProducesEmptyFilter_WhichMatchesEveryRecord_KNOWN_BUG()
        {
            // BUG conocido (reportado, no corregido acá): esta regla no tiene NINGUNA condición
            // anclada a ContactId — las dos únicas condiciones dependen de WitTramo y
            // WitIngresoBrutoFamiliar. Si el contact resuelto no tiene NINGUNO de los dos
            // seteados (caso común), Or(...) no agrega ninguna condición y el RecordFilter queda
            // con Conditions.Count == 0 — que por contrato de RecordFilter ("vacío significa sin
            // filtro, todos los registros", ver DataverseMasterDataMigrator.Core.Models.RecordFilter)
            // se interpreta como "sin filtro": TODOS los registros de wit_ingresofamiliarbruto
            // (de TODOS los sujetos) quedarían incluidos en la extracción de este sujeto. Este
            // test documenta el comportamiento actual a propósito, para que quede visible y
            // conste en el reporte — no es el comportamiento esperado por
            // docs/SUBJECT_RELATIONSHIP_MAP.md (que implica "ninguna condición = ningún
            // resultado" para wit_ingresofamiliarbruto).
            var ctx = MinimalContext(); // WitTramo y WitIngresoBrutoFamiliar ambos null
            var completamenteAjeno = SelfKeyed("wit_ingresofamiliarbruto", "wit_ingresofamiliarbrutoid");

            var ids = await MatchingIdsAsync(Rule("wit_ingresofamiliarbruto"), ctx, Data("wit_ingresofamiliarbruto", completamenteAjeno));

            Assert.Contains(completamenteAjeno.Id, ids); // comportamiento actual: sobre-incluye
        }

        // --- Fila 17: msdyn_ocliveworkitem -----------------------------------------------------------

        [Fact]
        public async Task Row17_OcLiveWorkItem_MatchesMsdynCustomer_AndRegardingObjectId_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var byCustomer = new DataRecord("msdyn_ocliveworkitem", Guid.NewGuid());
            SetLookup(byCustomer, "msdyn_customer", ctx.ContactId, "contact");
            var byRegarding = new DataRecord("msdyn_ocliveworkitem", Guid.NewGuid());
            SetLookup(byRegarding, "regardingobjectid", ctx.ContactId, "contact");
            var unrelated = Blank("msdyn_ocliveworkitem");

            var ids = await MatchingIdsAsync(
                Rule("msdyn_ocliveworkitem"), ctx,
                Data(("msdyn_ocliveworkitem", new[] { byCustomer, byRegarding, unrelated }), ("socialprofile", Array.Empty<DataRecord>())));

            Assert.Contains(byCustomer.Id, ids);
            Assert.Contains(byRegarding.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        [Fact]
        public async Task Row17_OcLiveWorkItem_MatchesOnlyViaSocialProfile_WhenNeitherDirectLookupIsSet()
        {
            // Caso especial pedido explícitamente: el msdyn_ocliveworkitem SOLO se relaciona por
            // la vía "socialprofile" — ni msdyn_customer ni regardingobjectid están seteados en
            // el registro. Hay que resolver socialprofile.customerid = ContactId, tomar el .Id
            // PROPIO de ESE socialprofile (es su primary key) y usarlo como
            // msdyn_socialprofileid del ocliveworkitem — no un GUID inventado.
            var ctx = FullContext();
            var socialProfile = new DataRecord("socialprofile", Guid.NewGuid());
            SetLookup(socialProfile, "customerid", ctx.ContactId, "contact");

            var viaSocialProfile = new DataRecord("msdyn_ocliveworkitem", Guid.NewGuid());
            SetLookup(viaSocialProfile, "msdyn_socialprofileid", socialProfile.Id, "socialprofile");
            // A propósito NO se setea msdyn_customer ni regardingobjectid en este record.

            var unrelatedWorkItem = new DataRecord("msdyn_ocliveworkitem", Guid.NewGuid());
            SetLookup(unrelatedWorkItem, "msdyn_socialprofileid", Guid.NewGuid(), "socialprofile");

            var data = Data(
                ("msdyn_ocliveworkitem", new[] { viaSocialProfile, unrelatedWorkItem }),
                ("socialprofile", new[] { socialProfile }));

            var ids = await MatchingIdsAsync(Rule("msdyn_ocliveworkitem"), ctx, data);

            Assert.Contains(viaSocialProfile.Id, ids);
            Assert.DoesNotContain(unrelatedWorkItem.Id, ids);
        }

        // --- Fila 18: activitymimeattachment -----------------------------------------------------------

        [Fact]
        public async Task Row18_ActivityMimeAttachment_MatchesOnlyViaEmailActivityId()
        {
            // Caso especial pedido explícitamente: activitymimeattachment.objectid apunta al
            // .Id PROPIO del email (su primary key ES activityid, no un atributo separado) —
            // nunca a un GUID inventado ni al ContactId directamente (esta tabla no tiene
            // ninguna condición directa sobre ContactId, ver fila 18 del documento).
            var ctx = FullContext();
            var email = new DataRecord("email", Guid.NewGuid());
            SetLookup(email, "regardingobjectid", ctx.ContactId, "contact");

            var viaEmail = new DataRecord("activitymimeattachment", Guid.NewGuid());
            SetLookup(viaEmail, "objectid", email.Id, "email");

            var unrelatedAttachment = new DataRecord("activitymimeattachment", Guid.NewGuid());
            SetLookup(unrelatedAttachment, "objectid", Guid.NewGuid(), "email");

            var data = Data(
                ("activitymimeattachment", new[] { viaEmail, unrelatedAttachment }),
                ("email", new[] { email }));

            var ids = await MatchingIdsAsync(Rule("activitymimeattachment"), ctx, data);

            Assert.Contains(viaEmail.Id, ids);
            Assert.DoesNotContain(unrelatedAttachment.Id, ids);
        }

        [Fact]
        public async Task Row18_ActivityMimeAttachment_NoMatchingEmails_ProducesFilterThatMatchesEveryRecord_KNOWN_BUG()
        {
            // BUG conocido (reportado, no corregido acá) — mismo patrón que la fila 16: esta
            // regla NO tiene ninguna condición directa sobre ContactId, solo el IN sobre
            // emailIds, y ese IN solo se agrega "if (emailIds.Count > 0)". Si el contacto no
            // tiene NINGÚN email (caso común), el filtro queda con Conditions.Count == 0, que
            // RecordFilter interpreta como "sin filtro" — TODOS los activitymimeattachment de
            // TODOS los sujetos quedarían incluidos.
            var ctx = FullContext();
            var completamenteAjeno = new DataRecord("activitymimeattachment", Guid.NewGuid());
            SetLookup(completamenteAjeno, "objectid", Guid.NewGuid(), "email");

            var data = Data(
                ("activitymimeattachment", new[] { completamenteAjeno }),
                ("email", Array.Empty<DataRecord>()));

            var ids = await MatchingIdsAsync(Rule("activitymimeattachment"), ctx, data);

            Assert.Contains(completamenteAjeno.Id, ids); // comportamiento actual: sobre-incluye
        }

        // --- Fila 19: socialprofile -----------------------------------------------------------------

        [Fact]
        public async Task Row19_SocialProfile_MatchesCustomerId_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var matching = new DataRecord("socialprofile", Guid.NewGuid());
            SetLookup(matching, "customerid", ctx.ContactId, "contact");
            var unrelated = Blank("socialprofile");

            var ids = await MatchingIdsAsync(Rule("socialprofile"), ctx, Data("socialprofile", matching, unrelated));

            Assert.Contains(matching.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        // --- Fila 20: wit_historicodatosofertaacademica -----------------------------------------------

        [Fact]
        public async Task Row20_HistoricoDatosOfertaAcademica_MatchesContactoId_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var matching = new DataRecord("wit_historicodatosofertaacademica", Guid.NewGuid());
            SetLookup(matching, "wit_contactoid", ctx.ContactId, "contact");
            var unrelated = Blank("wit_historicodatosofertaacademica");

            var ids = await MatchingIdsAsync(Rule("wit_historicodatosofertaacademica"), ctx, Data("wit_historicodatosofertaacademica", matching, unrelated));

            Assert.Contains(matching.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        // --- Fila 21: wit_historicocarreramatriculada -------------------------------------------------

        [Fact]
        public async Task Row21_HistoricoCarreraMatriculada_MatchesContacto_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var matching = new DataRecord("wit_historicocarreramatriculada", Guid.NewGuid());
            SetLookup(matching, "wit_contacto", ctx.ContactId, "contact");
            var unrelated = Blank("wit_historicocarreramatriculada");

            var ids = await MatchingIdsAsync(Rule("wit_historicocarreramatriculada"), ctx, Data("wit_historicocarreramatriculada", matching, unrelated));

            Assert.Contains(matching.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        // --- Fila 22: wit_contactodelacuenta ------------------------------------------------------------

        [Fact]
        public async Task Row22_ContactoDeLaCuenta_MatchesContacto_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var matching = new DataRecord("wit_contactodelacuenta", Guid.NewGuid());
            SetLookup(matching, "wit_contacto", ctx.ContactId, "contact");
            var unrelated = Blank("wit_contactodelacuenta");

            var ids = await MatchingIdsAsync(Rule("wit_contactodelacuenta"), ctx, Data("wit_contactodelacuenta", matching, unrelated));

            Assert.Contains(matching.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        // --- Fila 23: wit_whatsapp -----------------------------------------------------------------------

        [Fact]
        public async Task Row23_Whatsapp_MatchesRegardingObjectId_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var matching = new DataRecord("wit_whatsapp", Guid.NewGuid());
            SetLookup(matching, "regardingobjectid", ctx.ContactId, "contact");
            var unrelated = Blank("wit_whatsapp");

            var ids = await MatchingIdsAsync(Rule("wit_whatsapp"), ctx, Data("wit_whatsapp", matching, unrelated));

            Assert.Contains(matching.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        // --- Fila 24: wit_sms -----------------------------------------------------------------------------

        [Fact]
        public async Task Row24_Sms_MatchesRegardingObjectId_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var matching = new DataRecord("wit_sms", Guid.NewGuid());
            SetLookup(matching, "regardingobjectid", ctx.ContactId, "contact");
            var unrelated = Blank("wit_sms");

            var ids = await MatchingIdsAsync(Rule("wit_sms"), ctx, Data("wit_sms", matching, unrelated));

            Assert.Contains(matching.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        // --- Fila 25: activityparty --------------------------------------------------------------------

        [Fact]
        public async Task Row25_ActivityParty_MatchesPartyId_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var matching = new DataRecord("activityparty", Guid.NewGuid());
            SetLookup(matching, "partyid", ctx.ContactId, "contact");
            var unrelated = Blank("activityparty");

            var ids = await MatchingIdsAsync(Rule("activityparty"), ctx, Data("activityparty", matching, unrelated));

            Assert.Contains(matching.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        // --- Fila 26: activitypointer (vía activityparty.activityid) -------------------------------------

        [Fact]
        public async Task Row26_ActivityPointer_UsesActivityPartyLookupAttribute_NotItsOwnPrimaryKey()
        {
            // Caso especial pedido explícitamente, el más delicado del mapa: activityparty
            // tiene su PROPIA primary key (activitypartyid, que en el fake es su .Id) Y ADEMÁS
            // un atributo lookup "activityid" que apunta a la actividad real — DISTINTO de su
            // propia primary key. Si el código usara por error el .Id de activityparty en vez
            // del valor del atributo "activityid", este test lo detecta.
            var ctx = FullContext();
            var realActivityId = Guid.NewGuid();

            var activityParty = new DataRecord("activityparty", Guid.NewGuid()); // activitypartyid != realActivityId
            SetLookup(activityParty, "partyid", ctx.ContactId, "contact");
            SetLookup(activityParty, "activityid", realActivityId, "activitypointer");

            var correctActivity = SelfKeyed("activitypointer", "activityid", realActivityId);
            // Actividad "trampa": su propio .Id coincide con el .Id (activitypartyid) de
            // activityparty — si el código confundiera .Id por el atributo, esta sería la que
            // quedaría incluida por error en vez de correctActivity.
            var wrongActivityMatchingActivityPartysOwnId = SelfKeyed("activitypointer", "activityid", activityParty.Id);

            var data = Data(
                ("activityparty", new[] { activityParty }),
                ("activitypointer", new[] { correctActivity, wrongActivityMatchingActivityPartysOwnId }));

            var ids = await MatchingIdsAsync(Rule("activitypointer"), ctx, data);

            Assert.Contains(correctActivity.Id, ids);
            Assert.DoesNotContain(wrongActivityMatchingActivityPartysOwnId.Id, ids);
        }

        [Fact]
        public async Task Row26_ActivityPointer_NoMatchingActivityParty_ProducesFilterThatMatchesEveryRecord_KNOWN_BUG()
        {
            // BUG conocido (reportado, no corregido acá) — mismo patrón que las filas 16 y 18:
            // esta regla no tiene ninguna condición directa sobre ContactId, solo el IN sobre
            // activityIds, agregado "if (activityIds.Count > 0)". Si el contacto no aparece como
            // party de NINGUNA actividad (caso común), el filtro queda vacío y RecordFilter lo
            // interpreta como "sin filtro" — TODO activitypointer (de todos los sujetos)
            // quedaría incluido.
            var ctx = FullContext();
            var completamenteAjeno = SelfKeyed("activitypointer", "activityid");

            var data = Data(
                ("activityparty", Array.Empty<DataRecord>()),
                ("activitypointer", new[] { completamenteAjeno }));

            var ids = await MatchingIdsAsync(Rule("activitypointer"), ctx, data);

            Assert.Contains(completamenteAjeno.Id, ids); // comportamiento actual: sobre-incluye
        }

        // --- Fila 27: annotation (objectid=ContactId OR objectid IN incidents) ---------------------------

        [Fact]
        public async Task Row27_Annotation_MatchesDirectContactObjectId_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var direct = new DataRecord("annotation", Guid.NewGuid());
            SetLookup(direct, "objectid", ctx.ContactId, "contact");
            var unrelated = new DataRecord("annotation", Guid.NewGuid());
            SetLookup(unrelated, "objectid", Guid.NewGuid(), "contact");

            var data = Data(("annotation", new[] { direct, unrelated }), ("incident", Array.Empty<DataRecord>()));

            var ids = await MatchingIdsAsync(Rule("annotation"), ctx, data);

            Assert.Contains(direct.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        [Fact]
        public async Task Row27_Annotation_MatchesViaIncident_UsingSameRuleAsRow03_EvenWhenObjectIdIsNotContactId()
        {
            // Caso especial pedido explícitamente: un annotation cuyo objectid NO es el
            // ContactId sino el .Id de un incident real que matchea la MISMA regla que la fila 3
            // (acá vía customerid = ContactId) también debe quedar incluido.
            var ctx = FullContext();

            var incident = new DataRecord("incident", Guid.NewGuid());
            SetLookup(incident, "customerid", ctx.ContactId, "contact");

            var viaIncident = new DataRecord("annotation", Guid.NewGuid());
            SetLookup(viaIncident, "objectid", incident.Id, "incident");

            var unrelatedIncident = new DataRecord("incident", Guid.NewGuid());
            SetLookup(unrelatedIncident, "customerid", Guid.NewGuid(), "contact");
            var viaUnrelatedIncident = new DataRecord("annotation", Guid.NewGuid());
            SetLookup(viaUnrelatedIncident, "objectid", unrelatedIncident.Id, "incident");

            var data = Data(
                ("annotation", new[] { viaIncident, viaUnrelatedIncident }),
                ("incident", new[] { incident, unrelatedIncident }));

            var ids = await MatchingIdsAsync(Rule("annotation"), ctx, data);

            Assert.Contains(viaIncident.Id, ids);
            Assert.DoesNotContain(viaUnrelatedIncident.Id, ids);
        }

        [Fact]
        public async Task Row27_Annotation_WitCasoNull_DoesNotMatchAnnotationOnUnrelatedIncident()
        {
            var ctx = MinimalContext(); // WitCaso null, y ningún incident real está relacionado
            var unrelatedIncident = Blank("incident");
            var annotationOnUnrelatedIncident = new DataRecord("annotation", Guid.NewGuid());
            SetLookup(annotationOnUnrelatedIncident, "objectid", unrelatedIncident.Id, "incident");

            var data = Data(
                ("annotation", new[] { annotationOnUnrelatedIncident }),
                ("incident", new[] { unrelatedIncident }));

            var ids = await MatchingIdsAsync(Rule("annotation"), ctx, data);

            Assert.DoesNotContain(annotationOnUnrelatedIncident.Id, ids);
        }

        // --- Fila 28: customeraddress ----------------------------------------------------------------------

        [Fact]
        public async Task Row28_CustomerAddress_MatchesParentId_ExcludesUnrelated()
        {
            var ctx = FullContext();
            var matching = new DataRecord("customeraddress", Guid.NewGuid());
            SetLookup(matching, "parentid", ctx.ContactId, "contact");
            var unrelated = Blank("customeraddress");

            var ids = await MatchingIdsAsync(Rule("customeraddress"), ctx, Data("customeraddress", matching, unrelated));

            Assert.Contains(matching.Id, ids);
            Assert.DoesNotContain(unrelated.Id, ids);
        }

        // --- Cobertura general: 28 filas, ninguna de más, ninguna de menos ----------------------------------

        [Fact]
        public void Rules_ContainExactlyTheTwentyEightDocumentedTables_InDocumentedOrder()
        {
            var expected = new[]
            {
                "contact", "lead", "incident", "phonecall", "email", "wit_actividadchat",
                "wit_visitaweb", "wit_visitapresencial", "wit_evento", "wit_procesodepostulacion",
                "wit_solicituddeadmisiondirecta", "wit_historicocontactos",
                "wit_historicodatosdecontactabilidad", "wit_contactodelsegmentocomercial",
                "wit_colegio", "wit_ingresofamiliarbruto", "msdyn_ocliveworkitem",
                "activitymimeattachment", "socialprofile", "wit_historicodatosofertaacademica",
                "wit_historicocarreramatriculada", "wit_contactodelacuenta", "wit_whatsapp",
                "wit_sms", "activityparty", "activitypointer", "annotation", "customeraddress"
            };

            var actual = SubjectRelationshipMap.Rules.Select(r => r.LogicalName).ToArray();

            Assert.Equal(28, actual.Length);
            Assert.Equal(expected, actual);
        }
    }
}
