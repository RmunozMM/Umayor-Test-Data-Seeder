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
                rut: "11.111.111-1",
                originatingLeadId: leadId,
                witCaso: casoId,
                witEventoOrigen: eventoId,
                witColegio: colegioId,
                witTramo: tramoId,
                witIngresoBrutoFamiliar: ingresoId);

            var service = Service(contact);

            var ctx = await SubjectResolver.ResolveAsync(service, "11.111.111-1", null, CancellationToken.None);

            Assert.NotNull(ctx);
            Assert.Equal(contactId, ctx.ContactId);
            Assert.Equal("11.111.111-1", ctx.Rut);
            Assert.Equal(leadId, ctx.OriginatingLeadId);
            Assert.Equal(casoId, ctx.WitCaso);
            Assert.Equal(eventoId, ctx.WitEventoOrigen);
            Assert.Equal(colegioId, ctx.WitColegio);
            Assert.Equal(tramoId, ctx.WitTramo);
            Assert.Equal(ingresoId, ctx.WitIngresoBrutoFamiliar);
        }

        [Fact]
        public async Task ResolveAsync_ByPasaporte_FindsSameContactAsRut()
        {
            var contactId = Guid.NewGuid();
            var casoId = Guid.NewGuid();
            var contact = Contact(contactId, rut: "22.222.222-2", pasaporte: "BE316122", witCaso: casoId);
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
            var contact = Contact(contactId, rut: "33.333.333-3");
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
            var contact = Contact(Guid.NewGuid(), rut: "44.444.444-4");
            var service = Service(contact);

            var ctx = await SubjectResolver.ResolveAsync(service, "99.999.999-9", null, CancellationToken.None);

            Assert.Null(ctx);
        }

        [Fact]
        public async Task ResolveAsync_DuplicateRut_PicksMostRecentlyModifiedContact()
        {
            var olderId = Guid.NewGuid();
            var newerId = Guid.NewGuid();
            var older = Contact(olderId, rut: "55.555.555-5", modifiedOn: new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var newer = Contact(newerId, rut: "55.555.555-5", modifiedOn: new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
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
