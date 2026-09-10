using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataverseMasterDataMigrator.Core.Models;
using Umayor.TestDataSeeder.Core.SubjectGraph;
using Xunit;

namespace Umayor.TestDataSeeder.Tests.SubjectGraph
{
    /// <summary>
    /// Prueba <see cref="SubjectResolver"/> contra el bloque 1 del SQL de referencia
    /// ("RECUPERAR CONTACTO UNA SOLA VEZ") — ver docs/SUBJECT_RELATIONSHIP_MAP.md.
    /// </summary>
    public class SubjectResolverTests
    {
        private static DataRecord Contact(
            Guid id,
            string rut = null,
            string pasaporte = null,
            DateTime? modifiedOn = null,
            Guid? originatingLeadId = null,
            Guid? witCaso = null,
            Guid? witEventoOrigen = null,
            Guid? witColegio = null,
            Guid? witTramo = null,
            Guid? witIngresoBrutoFamiliar = null)
        {
            var record = new DataRecord("contact", id);
            if (rut != null) record.Attributes["wit_rut"] = rut;
            if (pasaporte != null) record.Attributes["wit_pasaporte"] = pasaporte;
            record.Attributes["modifiedon"] = modifiedOn ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            if (originatingLeadId.HasValue) record.Attributes["originatingleadid"] = new DataReference("lead", originatingLeadId.Value);
            if (witCaso.HasValue) record.Attributes["wit_caso"] = new DataReference("incident", witCaso.Value);
            if (witEventoOrigen.HasValue) record.Attributes["wit_eventoorigen"] = new DataReference("wit_evento", witEventoOrigen.Value);
            if (witColegio.HasValue) record.Attributes["wit_colegio"] = new DataReference("wit_colegio", witColegio.Value);
            if (witTramo.HasValue) record.Attributes["wit_tramo"] = new DataReference("wit_ingresofamiliarbruto", witTramo.Value);
            if (witIngresoBrutoFamiliar.HasValue) record.Attributes["wit_ingresobrutofamiliar"] = new DataReference("wit_ingresofamiliarbruto", witIngresoBrutoFamiliar.Value);
            return record;
        }

        private static FakeSourceRecordService Service(params DataRecord[] contacts)
            => new FakeSourceRecordService(new Dictionary<string, List<DataRecord>>(StringComparer.OrdinalIgnoreCase)
            {
                ["contact"] = new List<DataRecord>(contacts)
            });

        [Fact]
        public async Task ResolveAsync_ByRut_FindsContactAndResolvesAllFields()
        {
            var contactId = Guid.NewGuid();
            var leadId = Guid.NewGuid();
            var casoId = Guid.NewGuid();
            var eventoId = Guid.NewGuid();
            var colegioId = Guid.NewGuid();
            var tramoId = Guid.NewGuid();
            var ingresoId = Guid.NewGuid();

            var contact = Contact(
                contactId,
                rut: "11111111", // solo el cuerpo (8 dígitos), sin DV: así lo guarda contact.wit_rut en el tenant real (ver SubjectResolver.NormalizeRut)
                originatingLeadId: leadId,
                witCaso: casoId,
                witEventoOrigen: eventoId,
                witColegio: colegioId,
                witTramo: tramoId,
                witIngresoBrutoFamiliar: ingresoId);

            var service = Service(contact);

            // Búsqueda CON puntos/guión: prueba que NormalizeRut la deja apta para matchear el
            // valor sin puntuación que realmente guarda contact.wit_rut.
            var ctx = await SubjectResolver.ResolveAsync(service, "11.111.111-1", null, CancellationToken.None);

            Assert.NotNull(ctx);
            Assert.Equal(contactId, ctx.ContactId);
            Assert.Equal("11111111", ctx.Rut);
            Assert.Equal(leadId, ctx.OriginatingLeadId);
            Assert.Equal(casoId, ctx.WitCaso);
            Assert.Equal(eventoId, ctx.WitEventoOrigen);
            Assert.Equal(colegioId, ctx.WitColegio);
            Assert.Equal(tramoId, ctx.WitTramo);
            Assert.Equal(ingresoId, ctx.WitIngresoBrutoFamiliar);
        }

        /// <summary>
        /// Regresión de un bug real reportado por el usuario contra su tenant real: probó con
        /// "171752728" (cuerpo + DV concatenados sin guión, 9 dígitos) esperando que matcheara
        /// un contact cuyo wit_rut real es "17175272" (8 dígitos, SOLO el cuerpo — confirmado
        /// mirando el formulario real de Dataverse, con RUT y DV en campos separados). El primer
        /// intento de este fix asumía erróneamente que wit_rut guardaba cuerpo+DV concatenado;
        /// la corrección real es que <see cref="SubjectResolver.NormalizeRut"/> tiene que
        /// DESCARTAR el DV (todo lo que sigue al guión), no concatenarlo.
        /// </summary>
        [Theory]
        [InlineData("17175272-8")]   // formato esperado: cuerpo + guión + DV, se descarta el DV
        [InlineData("17.175.272-8")] // con puntos también
        [InlineData("17175272")]     // sin guión: se asume que ya es el cuerpo tal cual
        public async Task ResolveAsync_ByRut_AcceptsSeveralInputFormats_MatchesBodyOnlyWitRut(string typedRut)
        {
            var contactId = Guid.NewGuid();
            var contact = Contact(contactId, rut: "17175272"); // como lo guarda el tenant real: solo el cuerpo
            var service = Service(contact);

            var ctx = await SubjectResolver.ResolveAsync(service, typedRut, null, CancellationToken.None);

            Assert.NotNull(ctx);
            Assert.Equal(contactId, ctx.ContactId);
        }

        [Fact]
        public async Task ResolveAsync_ByRut_ConcatenatedBodyAndDv_WithoutDash_DoesNotMatch()
        {
            // "171752728" (cuerpo+DV pegados sin guión) NO debe matchear "17175272" (solo
            // cuerpo) — es exactamente el caso real que el usuario reportó como "la búsqueda no
            // está funcionando bien": antes de este fix el código asumía (mal) que esto SÍ debía
            // matchear.
            var contact = Contact(Guid.NewGuid(), rut: "17175272");
            var service = Service(contact);

            var ctx = await SubjectResolver.ResolveAsync(service, "171752728", null, CancellationToken.None);

            Assert.Null(ctx);
        }

        [Fact]
        public async Task ResolveAsync_ByPasaporte_FindsSameContactAsRut()
        {
            var contactId = Guid.NewGuid();
            var casoId = Guid.NewGuid();
            var contact = Contact(contactId, rut: "22222222", pasaporte: "BE316122", witCaso: casoId);
            var service = Service(contact);

            var byRut = await SubjectResolver.ResolveAsync(service, "22.222.222-2", null, CancellationToken.None);
            var byPasaporte = await SubjectResolver.ResolveAsync(service, null, "BE316122", CancellationToken.None);

            Assert.NotNull(byPasaporte);
            Assert.Equal(byRut.ContactId, byPasaporte.ContactId);
            Assert.Equal(byRut.Rut, byPasaporte.Rut);
            Assert.Equal(byRut.WitCaso, byPasaporte.WitCaso);
        }

        [Fact]
        public async Task ResolveAsync_ContactWithNullLookups_YieldsNullContextFields_DoesNotThrow()
        {
            var contactId = Guid.NewGuid();
            // Sin wit_caso, wit_eventoorigen, wit_colegio, wit_tramo, wit_ingresobrutofamiliar,
            // originatingleadid — ninguno seteado.
            var contact = Contact(contactId, rut: "33333333");
            var service = Service(contact);

            var ctx = await SubjectResolver.ResolveAsync(service, "33.333.333-3", null, CancellationToken.None);

            Assert.NotNull(ctx);
            Assert.Equal(contactId, ctx.ContactId);
            Assert.Null(ctx.OriginatingLeadId);
            Assert.Null(ctx.WitCaso);
            Assert.Null(ctx.WitEventoOrigen);
            Assert.Null(ctx.WitColegio);
            Assert.Null(ctx.WitTramo);
            Assert.Null(ctx.WitIngresoBrutoFamiliar);
        }

        [Fact]
        public async Task ResolveAsync_NoContactMatches_ReturnsNull_DoesNotThrow()
        {
            var contact = Contact(Guid.NewGuid(), rut: "44444444");
            var service = Service(contact);

            var ctx = await SubjectResolver.ResolveAsync(service, "99.999.999-9", null, CancellationToken.None);

            Assert.Null(ctx);
        }

        [Fact]
        public async Task ResolveAsync_DuplicateRut_PicksMostRecentlyModifiedContact()
        {
            var olderId = Guid.NewGuid();
            var newerId = Guid.NewGuid();
            var older = Contact(olderId, rut: "55555555", modifiedOn: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var newer = Contact(newerId, rut: "55555555", modifiedOn: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
            // Orden de inserción a propósito invertido para no depender de un orden estable que
            // "disimule" un bug de desempate.
            var service = Service(older, newer);

            var ctx = await SubjectResolver.ResolveAsync(service, "55.555.555-5", null, CancellationToken.None);

            Assert.NotNull(ctx);
            Assert.Equal(newerId, ctx.ContactId);
        }

        [Fact]
        public async Task ResolveAsync_NeitherRutNorPasaporte_ThrowsArgumentException()
        {
            var service = Service();

            await Assert.ThrowsAsync<ArgumentException>(
                () => SubjectResolver.ResolveAsync(service, null, "  ", CancellationToken.None));
        }
    }
}
