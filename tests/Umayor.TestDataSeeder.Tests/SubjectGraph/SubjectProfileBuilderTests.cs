using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataverseMasterDataMigrator.Core.Models;
using Umayor.TestDataSeeder.Core.SubjectGraph;
using Xunit;

namespace Umayor.TestDataSeeder.Tests.SubjectGraph
{
    /// <summary>
    /// Prueba de integración liviana: <see cref="SubjectProfileBuilder"/> une
    /// <see cref="SubjectResolver"/> + <see cref="SubjectRelationshipMap"/> en un solo
    /// <c>MigrationProfile</c> — acá solo se verifica el "pegamento" (28 entidades, una por
    /// tabla documentada, cada una con su <c>Filter</c> ya resuelto), no las reglas puntuales
    /// (esas están cubiertas fila por fila en <see cref="SubjectRelationshipMapTests"/>).
    /// </summary>
    public class SubjectProfileBuilderTests
    {
        [Fact]
        public async Task BuildAsync_NoMatchingContact_ReturnsNull()
        {
            var service = new FakeSourceRecordService(new Dictionary<string, List<DataRecord>>(StringComparer.OrdinalIgnoreCase)
            {
                ["contact"] = new List<DataRecord>()
            });

            var result = await SubjectProfileBuilder.BuildAsync(service, "00.000.000-0", null, CancellationToken.None);

            Assert.Null(result);
        }

        [Fact]
        public async Task BuildAsync_MatchingContact_ProducesOneEntityPerDocumentedTable_AllEnabled()
        {
            var contactId = Guid.NewGuid();
            var contact = new DataRecord("contact", contactId);
            contact.Attributes["wit_rut"] = "12345678";
            contact.Attributes["modifiedon"] = DateTime.UtcNow;

            var service = new FakeSourceRecordService(new Dictionary<string, List<DataRecord>>(StringComparer.OrdinalIgnoreCase)
            {
                ["contact"] = new List<DataRecord> { contact }
            });

            var result = await SubjectProfileBuilder.BuildAsync(service, "12.345.678-9", null, CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(contactId, result.Context.ContactId);
            Assert.Equal(28, result.Profile.Entities.Count);
            Assert.All(result.Profile.Entities, e => Assert.True(e.Enabled));
            Assert.All(result.Profile.Entities, e => Assert.NotNull(e.Filter));
            Assert.Equal(
                SubjectRelationshipMap.Rules.Select(r => r.LogicalName),
                result.Profile.Entities.Select(e => e.LogicalName));
        }

        /// <summary>
        /// Regresión del bug real encontrado en revisión: cuando una regla no logra resolver
        /// ninguna condición (acá, "wit_ingresofamiliarbruto" — el contacto no tiene
        /// wit_tramo ni wit_ingresobrutofamiliar, caso común), <see cref="RecordFilter"/> vacío
        /// significa contractualmente "sin filtro, todos los registros" — que en este selector
        /// filtraría datos de CUALQUIER otro sujeto hacia el entorno bajo. Confirma que
        /// <see cref="SubjectProfileBuilder"/> sustituye ese filtro vacío por uno que nunca
        /// matchea nada, no lo deja pasar tal cual.
        /// </summary>
        [Fact]
        public async Task BuildAsync_RuleWithNoResolvableCondition_NeverMatchesUnrelatedRecords()
        {
            var contactId = Guid.NewGuid();
            var contact = new DataRecord("contact", contactId);
            contact.Attributes["wit_rut"] = "12345678";
            contact.Attributes["modifiedon"] = DateTime.UtcNow;
            // Deliberadamente SIN wit_tramo ni wit_ingresobrutofamiliar.

            var unrelatedRecord = new DataRecord("wit_ingresofamiliarbruto", Guid.NewGuid());

            var service = new FakeSourceRecordService(new Dictionary<string, List<DataRecord>>(StringComparer.OrdinalIgnoreCase)
            {
                ["contact"] = new List<DataRecord> { contact }
            });

            var result = await SubjectProfileBuilder.BuildAsync(service, "12.345.678-9", null, CancellationToken.None);

            var entity = result.Profile.Entities.Single(e => e.LogicalName == "wit_ingresofamiliarbruto");

            Assert.True(entity.Filter.Conditions.Count > 0 || entity.Filter.SubFilters.Count > 0);
            Assert.False(FakeSourceRecordService.MatchesFilter(unrelatedRecord, entity.Filter));
        }
    }
}
