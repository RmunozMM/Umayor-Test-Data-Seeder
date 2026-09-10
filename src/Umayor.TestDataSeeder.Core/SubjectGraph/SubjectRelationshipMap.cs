using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataverseMasterDataMigrator.Core.Abstractions;
using DataverseMasterDataMigrator.Core.Models;

namespace Umayor.TestDataSeeder.Core.SubjectGraph
{
    /// <summary>
    /// Una regla del mapa: qué tabla, y cómo construir su <see cref="RecordFilter"/> a partir del
    /// <see cref="SubjectContext"/> resuelto. Las reglas "de 2 saltos" usan
    /// <paramref name="sourceRecords"/> para resolver una lista de IDs intermedios antes de armar
    /// el filtro final — cada una resuelve sus propios IDs de forma independiente, ninguna regla
    /// depende de que otra ya se haya ejecutado antes (ver docs/SUBJECT_RELATIONSHIP_MAP.md).
    /// </summary>
    public sealed class SubjectTableRule
    {
        public string LogicalName { get; set; }
        public Func<SubjectContext, IDataverseRecordService, CancellationToken, Task<RecordFilter>> BuildFilterAsync { get; set; }
    }

    /// <summary>
    /// Traducción 1:1 de <c>Ley_de_Proteccion_OPTIMIZADA_FAST_v4_pasaporte.sql</c> a reglas
    /// declarativas — ver docs/SUBJECT_RELATIONSHIP_MAP.md para la tabla completa y la
    /// justificación de cada una. Cualquier cambio acá debe reflejarse primero en ese documento.
    /// </summary>
    public static class SubjectRelationshipMap
    {
        public static IReadOnlyList<SubjectTableRule> Rules { get; } = BuildRules();

        private static IReadOnlyList<SubjectTableRule> BuildRules()
        {
            return new List<SubjectTableRule>
            {
                Direct("contact", ctx => Or(("contactid", ctx.ContactId), ("wit_rut", ctx.Rut))),
                Direct("lead", ctx => Or(("customerid", ctx.ContactId), ("parentcontactid", ctx.ContactId), ("leadid", ctx.OriginatingLeadId))),
                Direct("incident", BuildIncidentFilter),
                Direct("phonecall", ctx => OrWithRut(ctx, "regardingobjectid")),
                Direct("email", ctx => Or(("regardingobjectid", ctx.ContactId))),
                Direct("wit_actividadchat", ctx => OrWithRut(ctx, "regardingobjectid")),
                Direct("wit_visitaweb", ctx => Or(("regardingobjectid", ctx.ContactId))),
                Direct("wit_visitapresencial", ctx => Or(("regardingobjectid", ctx.ContactId))),
                Direct("wit_evento", ctx => Or(("regardingobjectid", ctx.ContactId), ("activityid", ctx.WitEventoOrigen))),
                Direct("wit_procesodepostulacion", ctx => Or(("wit_contacto", ctx.ContactId), ("wit_referente", ctx.ContactId))),
                Direct("wit_solicituddeadmisiondirecta", ctx => OrWithRut(ctx, "wit_postulante")),
                Direct("wit_historicocontactos", ctx => Or(("wit_contact", ctx.ContactId), ("wit_contactorelacionado", ctx.ContactId))),
                Direct("wit_historicodatosdecontactabilidad", ctx => OrWithRut(ctx, "wit_contacto")),
                Direct("wit_contactodelsegmentocomercial", ctx => OrWithRut(ctx, "wit_contacto")),
                Direct("wit_colegio", ctx => Or(
                    ("wit_colegioid", ctx.WitColegio),
                    ("wit_coordinador", ctx.ContactId),
                    ("wit_director", ctx.ContactId),
                    ("wit_encargado", ctx.ContactId),
                    ("wit_orientador", ctx.ContactId))),
                Direct("wit_ingresofamiliarbruto", ctx => Or(
                    ("wit_ingresofamiliarbrutoid", ctx.WitTramo),
                    ("wit_ingresofamiliarbrutoid", ctx.WitIngresoBrutoFamiliar))),
                new SubjectTableRule { LogicalName = "msdyn_ocliveworkitem", BuildFilterAsync = BuildOcLiveWorkItemFilterAsync },
                new SubjectTableRule { LogicalName = "activitymimeattachment", BuildFilterAsync = BuildActivityMimeAttachmentFilterAsync },
                Direct("socialprofile", ctx => Or(("customerid", ctx.ContactId))),
                Direct("wit_historicodatosofertaacademica", ctx => Or(("wit_contactoid", ctx.ContactId))),
                Direct("wit_historicocarreramatriculada", ctx => Or(("wit_contacto", ctx.ContactId))),
                Direct("wit_contactodelacuenta", ctx => Or(("wit_contacto", ctx.ContactId))),
                Direct("wit_whatsapp", ctx => Or(("regardingobjectid", ctx.ContactId))),
                Direct("wit_sms", ctx => Or(("regardingobjectid", ctx.ContactId))),
                Direct("activityparty", ctx => Or(("partyid", ctx.ContactId))),
                new SubjectTableRule { LogicalName = "activitypointer", BuildFilterAsync = BuildActivityPointerFilterAsync },
                new SubjectTableRule { LogicalName = "annotation", BuildFilterAsync = BuildAnnotationFilterAsync },
                Direct("customeraddress", ctx => Or(("parentid", ctx.ContactId))),
            };
        }

        // --- Reglas directas (sin consulta intermedia) ---------------------------------------

        private static RecordFilter BuildIncidentFilter(SubjectContext ctx) => Or(
            ("customerid", ctx.ContactId),
            ("primarycontactid", ctx.ContactId),
            ("responsiblecontactid", ctx.ContactId),
            ("msa_partnercontactid", ctx.ContactId),
            ("incidentid", ctx.WitCaso));

        /// <summary>Patrón repetido varias veces en el SQL: "regardingobjectid/campo propio =
        /// ContactId OR wit_rut = Rut" — varias tablas tienen su propia columna <c>wit_rut</c>
        /// denormalizada y se buscan también por ese valor, no solo por <see cref="SubjectContext.ContactId"/>
        /// (cubre registros que quedaron con el lookup vacío pero el RUT sí cargado a mano).</summary>
        private static RecordFilter OrWithRut(SubjectContext ctx, string ownAttribute)
            => Or((ownAttribute, ctx.ContactId), ("wit_rut", ctx.Rut));

        private static RecordFilter Or(params (string Attribute, object Value)[] conditions)
        {
            var filter = new RecordFilter { LogicalOperator = FilterLogicalOperator.Or };
            foreach (var (attribute, value) in conditions)
            {
                if (value == null) continue;
                filter.Conditions.Add(new FilterCondition { AttributeName = attribute, Operator = FilterOperator.Equal, Value = value });
            }
            return filter;
        }

        private static SubjectTableRule Direct(string logicalName, Func<SubjectContext, RecordFilter> build)
            => new SubjectTableRule
            {
                LogicalName = logicalName,
                BuildFilterAsync = (ctx, _, __) => Task.FromResult(build(ctx))
            };

        // --- Reglas de 2 saltos (necesitan una consulta previa) -------------------------------

        private static async Task<RecordFilter> BuildOcLiveWorkItemFilterAsync(SubjectContext ctx, IDataverseRecordService sourceRecords, CancellationToken ct)
        {
            var filter = Or(("msdyn_customer", ctx.ContactId), ("regardingobjectid", ctx.ContactId));

            // "vía socialprofile": msdyn_ocliveworkitem.msdyn_socialprofileid IN
            // (socialprofile.socialprofileid WHERE customerid = ContactId). socialprofileid ES
            // la propia primary key de socialprofile, así que se usa el .Id del record (no un
            // atributo lookup) — a diferencia de la regla de activityparty más abajo.
            var socialProfileIds = await ResolveOwnIdsAsync(sourceRecords, "socialprofile", Or(("customerid", ctx.ContactId)), ct).ConfigureAwait(false);
            if (socialProfileIds.Count > 0)
                filter.Conditions.Add(new FilterCondition { AttributeName = "msdyn_socialprofileid", Operator = FilterOperator.In, Value = socialProfileIds });

            return filter;
        }

        private static async Task<RecordFilter> BuildActivityMimeAttachmentFilterAsync(SubjectContext ctx, IDataverseRecordService sourceRecords, CancellationToken ct)
        {
            // activitymimeattachment.objectid IN (email.activityid WHERE regardingobjectid =
            // ContactId). El primary key de "email" (un tipo Activity) ES su activityid, así que
            // el .Id del record ya es el valor correcto — igual razón que socialprofile arriba.
            var emailIds = await ResolveOwnIdsAsync(sourceRecords, "email", Or(("regardingobjectid", ctx.ContactId)), ct).ConfigureAwait(false);

            var filter = new RecordFilter { LogicalOperator = FilterLogicalOperator.Or };
            if (emailIds.Count > 0)
                filter.Conditions.Add(new FilterCondition { AttributeName = "objectid", Operator = FilterOperator.In, Value = emailIds });
            return filter;
        }

        private static async Task<RecordFilter> BuildActivityPointerFilterAsync(SubjectContext ctx, IDataverseRecordService sourceRecords, CancellationToken ct)
        {
            // activitypointer.activityid IN (activityparty.activityid WHERE partyid =
            // ContactId). A diferencia de las dos reglas de arriba: acá "activityid" es un
            // ATRIBUTO LOOKUP de activityparty (apunta a la actividad), NO su propia primary key
            // (que es activitypartyid) — hay que leer el valor del atributo, no el .Id del
            // record. Confundir esto es el error más fácil de cometer en todo este mapa.
            var activityIds = await ResolveReferencedIdsAsync(sourceRecords, "activityparty", "activityid", Or(("partyid", ctx.ContactId)), ct).ConfigureAwait(false);

            var filter = new RecordFilter { LogicalOperator = FilterLogicalOperator.Or };
            if (activityIds.Count > 0)
                filter.Conditions.Add(new FilterCondition { AttributeName = "activityid", Operator = FilterOperator.In, Value = activityIds });
            return filter;
        }

        private static async Task<RecordFilter> BuildAnnotationFilterAsync(SubjectContext ctx, IDataverseRecordService sourceRecords, CancellationToken ct)
        {
            // Fusiona los dos UNION ALL del SQL ("annotation_contact" y "annotation_incident") —
            // son la misma tabla, con dos orígenes distintos de objectid.
            var filter = Or(("objectid", ctx.ContactId));

            var incidentIds = await ResolveOwnIdsAsync(sourceRecords, "incident", BuildIncidentFilter(ctx), ct).ConfigureAwait(false);
            if (incidentIds.Count > 0)
                filter.Conditions.Add(new FilterCondition { AttributeName = "objectid", Operator = FilterOperator.In, Value = incidentIds });

            return filter;
        }

        // --- Helpers de resolución de IDs intermedios -----------------------------------------

        /// <summary>Trae los <c>.Id</c> (primary key) de los registros de <paramref name="logicalName"/>
        /// que matchean <paramref name="filter"/>. Usar cuando el ID que se necesita ES la
        /// primary key de esa tabla (p. ej. socialprofileid, o el activityid de una Activity que
        /// coincide con su propio primary key).</summary>
        private static async Task<List<Guid>> ResolveOwnIdsAsync(
            IDataverseRecordService sourceRecords, string logicalName, RecordFilter filter, CancellationToken ct)
        {
            var ids = new List<Guid>();
            string pageToken = null;
            do
            {
                ct.ThrowIfCancellationRequested();
                var page = await sourceRecords
                    .RetrieveFilteredPageAsync(logicalName, Array.Empty<string>(), filter, pageToken, pageSize: 500, ct)
                    .ConfigureAwait(false);
                ids.AddRange(page.Records.Select(r => r.Id));
                pageToken = page.HasMore ? page.NextPageToken : null;
            }
            while (pageToken != null);
            return ids;
        }

        /// <summary>Trae el valor de un atributo lookup (<paramref name="lookupAttribute"/>) de
        /// los registros de <paramref name="logicalName"/> que matchean <paramref name="filter"/>
        /// — usar cuando el ID que se necesita NO es la primary key de esa tabla, sino un GUID
        /// que cada fila referencia (p. ej. activityparty.activityid).</summary>
        private static async Task<List<Guid>> ResolveReferencedIdsAsync(
            IDataverseRecordService sourceRecords, string logicalName, string lookupAttribute, RecordFilter filter, CancellationToken ct)
        {
            var ids = new List<Guid>();
            string pageToken = null;
            do
            {
                ct.ThrowIfCancellationRequested();
                var page = await sourceRecords
                    .RetrieveFilteredPageAsync(logicalName, new[] { lookupAttribute }, filter, pageToken, pageSize: 500, ct)
                    .ConfigureAwait(false);
                foreach (var record in page.Records)
                {
                    if (record.TryGetValue<DataReference>(lookupAttribute, out var reference))
                        ids.Add(reference.Id);
                }
                pageToken = page.HasMore ? page.NextPageToken : null;
            }
            while (pageToken != null);
            return ids;
        }
    }
}
