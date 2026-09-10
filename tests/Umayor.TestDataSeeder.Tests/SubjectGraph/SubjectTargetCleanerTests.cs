using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataverseMasterDataMigrator.Core.Abstractions;
using DataverseMasterDataMigrator.Core.Migration;
using DataverseMasterDataMigrator.Core.Models;
using Umayor.TestDataSeeder.Core.SubjectGraph;
using Xunit;

namespace Umayor.TestDataSeeder.Tests.SubjectGraph
{
    /// <summary>
    /// Prueba <see cref="SubjectTargetCleaner"/> contra <see cref="FakeSourceRecordService"/>
    /// actuando como Target (mismo fake usado para SubjectResolver/SubjectRelationshipMap, ahora
    /// también con un <c>DeleteBatchAsync</c> funcional — ver su doc-comment).
    /// </summary>
    public class SubjectTargetCleanerTests
    {
        private static TableSummary Table(string logicalName) => new TableSummary
        {
            LogicalName = logicalName,
            PrimaryIdAttribute = logicalName + "id"
        };

        private static ProfileEntity Entity(string logicalName, int order, RecordFilter filter = null) => new ProfileEntity
        {
            LogicalName = logicalName,
            Enabled = true,
            PreferredOrder = order,
            Filter = filter
        };

        /// <summary>Filtro no-vacío mínimo — el mismo tipo de <see cref="RecordFilter"/> que
        /// <c>SubjectProfileBuilder.BuildAsync</c> produce en el camino real, para que los tests
        /// que no prueban la guarda de filtro vacío/nulo directamente no dependan de ese caso
        /// borde por accidente.</summary>
        private static RecordFilter IdFilter(string idAttribute, Guid id) => new RecordFilter
        {
            LogicalOperator = FilterLogicalOperator.And,
            Conditions = new List<FilterCondition>
            {
                new FilterCondition { AttributeName = idAttribute, Operator = FilterOperator.Equal, Value = id }
            }
        };

        [Fact]
        public async Task DeleteFromTargetAsync_RemovesMatchingRecords_AndOrdersTablesInReverse()
        {
            var contactId = Guid.NewGuid();
            var caseId = Guid.NewGuid();

            var contactRecord = new DataRecord("contact", contactId);
            // FakeSourceRecordService evalúa un FilterCondition contra record.Attributes, no
            // contra record.Id — a diferencia de Dataverse real, donde una condición sobre el
            // atributo de primary key sí compara contra el id real del registro. Hay que
            // reflejar el id acá explícitamente para que el filtro de este test (representativo
            // del que arma SubjectProfileBuilder.BuildAsync en el camino real) matchee.
            contactRecord.Attributes["contactid"] = contactId;
            var caseRecord = new DataRecord("incident", caseId);
            caseRecord.Attributes["incidentid"] = caseId;
            caseRecord.Attributes["customerid"] = new DataReference("contact", contactId);

            var data = new Dictionary<string, List<DataRecord>>(StringComparer.OrdinalIgnoreCase)
            {
                ["contact"] = new List<DataRecord> { contactRecord },
                ["incident"] = new List<DataRecord> { caseRecord }
            };
            var service = new FakeSourceRecordService(data);

            // "contact" se crea antes que "incident" (PreferredOrder 0 vs 1) — el borrado debe
            // procesar "incident" primero (orden inverso al de creación). Filtros no-vacíos
            // (como los que arma SubjectProfileBuilder.BuildAsync en el camino real) — un Filter
            // null/vacío ahora es rechazado por la guarda defensiva de SubjectTargetCleaner
            // (ver DeleteFromTargetAsync_EntityWithEmptyOrNullFilter_SkipsTableWithoutAnyNetworkCall).
            var profile = new MigrationProfile
            {
                Entities = new List<ProfileEntity>
                {
                    Entity("contact", order: 0, filter: IdFilter("contactid", contactId)),
                    Entity("incident", order: 1, filter: IdFilter("incidentid", caseId))
                }
            };

            var targetTables = new Dictionary<string, TableSummary>(StringComparer.OrdinalIgnoreCase)
            {
                ["contact"] = Table("contact"),
                ["incident"] = Table("incident")
            };

            var progress = new List<string>();

            var results = await SubjectTargetCleaner.DeleteFromTargetAsync(
                profile, targetTables, service, msg => progress.Add(msg), CancellationToken.None);

            Assert.Equal(new[] { "incident", "contact" }, results.Select(r => r.LogicalName).ToArray());
            Assert.All(results, r => Assert.Equal(0, r.Failed));
            Assert.Equal(1, results.Single(r => r.LogicalName == "incident").Deleted);
            Assert.Equal(1, results.Single(r => r.LogicalName == "contact").Deleted);

            Assert.Empty(data["contact"]);
            Assert.Empty(data["incident"]);
        }

        [Fact]
        public async Task DeleteFromTargetAsync_TableWithNoMatchingRecords_ReportsZeroDeletedAndDoesNotFail()
        {
            var data = new Dictionary<string, List<DataRecord>>(StringComparer.OrdinalIgnoreCase)
            {
                ["contact"] = new List<DataRecord>()
            };
            var service = new FakeSourceRecordService(data);

            // Filtro no-vacío (real): la tabla está vacía en Target, así que RetrieveFilteredPageAsync
            // sí se ejecuta pero no matchea nada — a diferencia de un Filter null/vacío, que la
            // guarda defensiva saltearía ANTES de siquiera consultar Target.
            var profile = new MigrationProfile
            {
                Entities = new List<ProfileEntity> { Entity("contact", order: 0, filter: IdFilter("contactid", Guid.NewGuid())) }
            };
            var targetTables = new Dictionary<string, TableSummary>(StringComparer.OrdinalIgnoreCase)
            {
                ["contact"] = Table("contact")
            };

            var results = await SubjectTargetCleaner.DeleteFromTargetAsync(
                profile, targetTables, service, null, CancellationToken.None);

            var single = Assert.Single(results);
            Assert.Equal("contact", single.LogicalName);
            Assert.Equal(0, single.Deleted);
            Assert.Equal(0, single.Failed);
        }

        [Fact]
        public async Task DeleteFromTargetAsync_DeletingAnAlreadyMissingRecord_IsIdempotentSuccess()
        {
            // FakeSourceRecordService.DeleteBatchAsync reporta éxito para cualquier id pedido,
            // exista o no en _data — mismo contrato que la implementación real contra Dataverse
            // (0x80040217 "no existe" se mapea a Succeeded, no a Failed).
            var contactId = Guid.NewGuid();
            var contactRecord = new DataRecord("contact", contactId);
            contactRecord.Attributes["contactid"] = contactId;
            var data = new Dictionary<string, List<DataRecord>>(StringComparer.OrdinalIgnoreCase)
            {
                ["contact"] = new List<DataRecord> { contactRecord }
            };
            var service = new FakeSourceRecordService(data);

            // Se borra "manualmente" antes de invocar el cleaner, simulando que ya no está en Target.
            data["contact"].Clear();

            var profile = new MigrationProfile
            {
                Entities = new List<ProfileEntity> { Entity("contact", order: 0, filter: IdFilter("contactid", contactId)) }
            };
            var targetTables = new Dictionary<string, TableSummary>(StringComparer.OrdinalIgnoreCase)
            {
                ["contact"] = Table("contact")
            };

            var results = await SubjectTargetCleaner.DeleteFromTargetAsync(
                profile, targetTables, service, null, CancellationToken.None);

            var single = Assert.Single(results);
            Assert.Equal(0, single.Deleted);
            Assert.Equal(0, single.Failed);
        }

        /// <summary>
        /// Prueba la guarda defensiva agregada en <see cref="SubjectTargetCleaner"/> (defensa en
        /// profundidad frente al guard de <c>SubjectProfileBuilder.IsEmpty</c>, que en el camino
        /// normal ya nunca deja pasar un <c>Filter</c> vacío): si a pesar de eso una
        /// <see cref="ProfileEntity"/> llega con <c>Filter = null</c> (o un <see cref="RecordFilter"/>
        /// sin Conditions ni SubFilters), esa tabla debe SALTEARSE por completo — cero llamadas de
        /// red (ni de lectura ni de borrado) y cero registros eliminados — en vez de interpretarlo
        /// como "sin filtro, toda la tabla" (el comportamiento contractual real de
        /// <see cref="RecordFilter"/> para el migrador genérico, que acá sería catastrófico:
        /// borraría TODOS los registros de la tabla en Target, no solo los del sujeto).
        /// Usa <see cref="ThrowingRecordService"/> en vez de <see cref="FakeSourceRecordService"/>
        /// para que cualquier intento de red (aunque sea sobre una tabla que sí tiene datos)
        /// tumbe el test inmediatamente, dejando sin ambigüedad que la guarda actuó ANTES de
        /// cualquier llamada, no que "coincidentemente" no encontró nada.
        /// </summary>
        [Fact]
        public async Task DeleteFromTargetAsync_EntityWithEmptyOrNullFilter_SkipsTableWithoutAnyNetworkCall()
        {
            var service = new ThrowingRecordService();

            var profile = new MigrationProfile
            {
                Entities = new List<ProfileEntity>
                {
                    Entity("contact", order: 0, filter: null),
                    Entity("incident", order: 1, filter: new RecordFilter
                    {
                        LogicalOperator = FilterLogicalOperator.And,
                        Conditions = new List<FilterCondition>(),
                        SubFilters = new List<RecordFilter>()
                    })
                }
            };

            var targetTables = new Dictionary<string, TableSummary>(StringComparer.OrdinalIgnoreCase)
            {
                ["contact"] = Table("contact"),
                ["incident"] = Table("incident")
            };

            var progress = new List<string>();

            var results = await SubjectTargetCleaner.DeleteFromTargetAsync(
                profile, targetTables, service, msg => progress.Add(msg), CancellationToken.None);

            Assert.Equal(2, results.Count);
            Assert.All(results, r => Assert.Equal(0, r.Deleted));
            Assert.All(results, r => Assert.Equal(0, r.Failed));
            Assert.Equal(0, service.RetrieveFilteredPageCalls);
            Assert.Equal(0, service.DeleteBatchCalls);
            Assert.Contains(progress, msg => msg.Contains("contact") && msg.Contains("SALTEADA"));
            Assert.Contains(progress, msg => msg.Contains("incident") && msg.Contains("SALTEADA"));
        }

        /// <summary>
        /// <see cref="IDataverseRecordService"/> que cuenta llamadas a los dos métodos que
        /// <see cref="SubjectTargetCleaner"/> usa para leer/borrar, y lanza si de verdad se
        /// invocan — el test de la guarda defensiva necesita probar "cero llamadas de red", no
        /// solo "cero registros borrados" (que también pasaría, sin querer, si la tabla
        /// simplemente estuviera vacía).
        /// </summary>
        private sealed class ThrowingRecordService : IDataverseRecordService
        {
            public int RetrieveFilteredPageCalls { get; private set; }
            public int DeleteBatchCalls { get; private set; }

            public Task<RecordPage> RetrievePageAsync(
                string logicalName, IReadOnlyList<string> columns, string pageToken, int pageSize, CancellationToken cancellationToken)
                => RetrieveFilteredPageAsync(logicalName, columns, null, pageToken, pageSize, cancellationToken);

            public Task<RecordPage> RetrieveFilteredPageAsync(
                string logicalName, IReadOnlyList<string> columns, RecordFilter filter, string pageToken, int pageSize, CancellationToken cancellationToken)
            {
                RetrieveFilteredPageCalls++;
                throw new InvalidOperationException($"No debería haberse llamado a RetrieveFilteredPageAsync('{logicalName}') — la guarda defensiva de filtro vacío debía saltear esta tabla antes de cualquier llamada de red.");
            }

            public Task<IReadOnlyList<DataRecord>> RetrieveByIdsAsync(
                string logicalName, IReadOnlyList<Guid> ids, IReadOnlyList<string> columns, CancellationToken cancellationToken)
                => throw new NotSupportedException();

            public Task<bool> ExistsAsync(DataReference reference, CancellationToken cancellationToken)
                => throw new NotSupportedException();

            public Task<int> GetApproximateCountAsync(string logicalName, CancellationToken cancellationToken)
                => throw new NotSupportedException();

            public Task<IReadOnlyList<RecordOperationResult>> WriteBatchAsync(
                string logicalName, IReadOnlyList<DataRecord> batch, WriteStrategy strategy, int pass, CancellationToken cancellationToken)
                => throw new NotSupportedException();

            public Task AssociateAsync(
                string relationshipSchemaName, DataReference from, IReadOnlyList<DataReference> to, CancellationToken cancellationToken)
                => throw new NotSupportedException();

            public Task<IReadOnlyList<RecordOperationResult>> DeleteBatchAsync(
                string logicalName, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
            {
                DeleteBatchCalls++;
                throw new InvalidOperationException($"No debería haberse llamado a DeleteBatchAsync('{logicalName}') — la guarda defensiva de filtro vacío debía saltear esta tabla antes de cualquier llamada de red.");
            }
        }
    }
}
