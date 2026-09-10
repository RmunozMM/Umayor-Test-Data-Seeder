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
        public async Task BuildAsync_MatchingContact_ProducesOneEntityPerDocumentedTable()
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
            Assert.All(result.Profile.Entities, e => Assert.NotNull(e.Filter));
            Assert.Equal(
                SubjectRelationshipMap.Rules.Select(r => r.LogicalName),
                result.Profile.Entities.Select(e => e.LogicalName));
        }

        /// <summary>
        /// Regresión de un crash real: "The 'Create' method does not support entities of type
        /// 'activitypointer'" / "...'activityparty'" — Dataverse no permite crear directamente
        /// ninguna de las dos (activitypointer es la vista base polimórfica de cualquier
        /// actividad concreta ya migrada por separado; activityparty se crea implícitamente al
        /// setear los campos to/from de una actividad). Deben quedar en el perfil pero
        /// deshabilitadas — el resto de las 26 tablas sigue habilitado.
        /// </summary>
        [Fact]
        public async Task BuildAsync_ActivityPointerAndActivityParty_AreDisabled_RestAreEnabled()
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

            var nonWritable = new[] { "activitypointer", "activityparty" };
            foreach (var logicalName in nonWritable)
            {
                var entity = result.Profile.Entities.Single(e => e.LogicalName == logicalName);
                Assert.False(entity.Enabled);
            }

            var writable = result.Profile.Entities.Where(e => !nonWritable.Contains(e.LogicalName, StringComparer.OrdinalIgnoreCase));
            Assert.All(writable, e => Assert.True(e.Enabled));
        }

        /// <summary>
        /// Regresión de un crash en cascada real: un lookup externo al perfil (fuera de las 28
        /// tablas del mapa) sin ese GUID en Target tumbaba la escritura de "contact" entero, y
        /// como contact nunca se creaba, TODO lo que depende de él fallaba también — 336 de 337
        /// registros en una corrida real. La política por defecto de este perfil debe ser
        /// SkipSilently (Required Y Optional), a diferencia del migrador genérico.
        /// </summary>
        [Fact]
        public async Task BuildAsync_Profile_DefaultsToSkipSilentlyForBothLookupPolicies()
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

            Assert.Equal(LookupPolicy.SkipSilently, result.Profile.Options.RequiredLookupPolicy);
            Assert.Equal(LookupPolicy.SkipSilently, result.Profile.Options.OptionalLookupPolicy);
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

        /// <summary>
        /// Regresión de un crash real reportado por el usuario contra su tenant real: "'ActivityPointer'
        /// entity doesn't contain attribute with Name = 'activitypointerid'". Causa: el guard de
        /// filtro vacío armaba la condición "no-match" con "&lt;logicalname&gt;id" — pero
        /// <c>activitypointer</c>, como TODA entidad de tipo Activity en Dataverse, tiene su
        /// primary key real en <c>activityid</c>, no en "&lt;logicalname&gt;id". Se dispara
        /// exactamente en el caso más común: un contacto sin ningún <c>activityparty</c> propio
        /// (la única vía de resolución de la fila 26).
        /// </summary>
        [Fact]
        public async Task BuildAsync_ActivityPointerNoMatchFilter_UsesActivityIdNotOwnLogicalNameId()
        {
            var contactId = Guid.NewGuid();
            var contact = new DataRecord("contact", contactId);
            contact.Attributes["wit_rut"] = "12345678";
            contact.Attributes["modifiedon"] = DateTime.UtcNow;
            // Deliberadamente sin ningún activityparty para este contacto -> BuildActivityPointerFilterAsync
            // devuelve un filtro vacío -> dispara el guard NoMatchFilter de SubjectProfileBuilder.

            var service = new FakeSourceRecordService(new Dictionary<string, List<DataRecord>>(StringComparer.OrdinalIgnoreCase)
            {
                ["contact"] = new List<DataRecord> { contact }
            });

            var result = await SubjectProfileBuilder.BuildAsync(service, "12.345.678-9", null, CancellationToken.None);

            var entity = result.Profile.Entities.Single(e => e.LogicalName == "activitypointer");
            var condition = Assert.Single(entity.Filter.Conditions);

            Assert.Equal("activityid", condition.AttributeName);
            Assert.NotEqual("activitypointerid", condition.AttributeName);
        }
    }
}
