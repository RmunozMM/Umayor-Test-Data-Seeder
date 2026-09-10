using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataverseMasterDataMigrator.Core.Abstractions;
using DataverseMasterDataMigrator.Core.Migration;
using DataverseMasterDataMigrator.Core.Models;

namespace Umayor.TestDataSeeder.Tests.SubjectGraph
{
    /// <summary>
    /// Fake de <see cref="IDataverseRecordService"/> para probar <c>SubjectResolver</c>,
    /// <c>SubjectRelationshipMap</c> y <c>SubjectProfileBuilder</c> sin Dataverse real.
    ///
    /// Evalúa <see cref="RecordFilter"/> en memoria contra <see cref="DataRecord.Attributes"/>
    /// con el mismo comportamiento que la implementación real (ver
    /// <c>DataverseRecordServiceAdapter.BuildFilterExpression</c> en el proyecto hermano):
    /// - Un valor lookup guardado como <see cref="DataReference"/> se compara por su
    ///   <c>.Id</c> (así una condición con <c>Value = Guid</c> matchea un atributo guardado como
    ///   <see cref="DataReference"/>, igual que un <c>ConditionExpression</c> real de Dataverse
    ///   comparando un lookup contra un GUID).
    /// - Un valor <c>string</c> con <see cref="FilterOperator.In"/> NUNCA se trata como
    ///   <c>IEnumerable&lt;char&gt;</c> — mismo bug ya corregido en el proyecto hermano
    ///   (<c>DataverseRecordServiceAdapter.BuildFilterExpression</c>), reproducido acá a
    ///   propósito para que un test de este proyecto lo pueda detectar si reaparece.
    ///
    /// Solo implementa <see cref="RetrieveFilteredPageAsync"/> (y <see cref="RetrievePageAsync"/>,
    /// que delega en el anterior sin filtro) con datos reales; el resto de la interfaz lanza
    /// <see cref="NotSupportedException"/> porque ningún test de <c>SubjectGraph</c> los necesita.
    /// </summary>
    public sealed class FakeSourceRecordService : IDataverseRecordService
    {
        private readonly Dictionary<string, List<DataRecord>> _data;

        public FakeSourceRecordService(Dictionary<string, List<DataRecord>> data)
        {
            _data = data ?? new Dictionary<string, List<DataRecord>>(StringComparer.OrdinalIgnoreCase);
        }

        public Task<RecordPage> RetrievePageAsync(
            string logicalName, IReadOnlyList<string> columns, string pageToken, int pageSize, CancellationToken cancellationToken)
            => RetrieveFilteredPageAsync(logicalName, columns, null, pageToken, pageSize, cancellationToken);

        public Task<RecordPage> RetrieveFilteredPageAsync(
            string logicalName, IReadOnlyList<string> columns, RecordFilter filter, string pageToken, int pageSize, CancellationToken cancellationToken)
        {
            _data.TryGetValue(logicalName, out var list);
            var matches = (list ?? new List<DataRecord>()).Where(r => MatchesFilter(r, filter)).ToList();
            return Task.FromResult(new RecordPage { Records = matches, HasMore = false, NextPageToken = null });
        }

        public Task<IReadOnlyList<DataRecord>> RetrieveByIdsAsync(
            string logicalName, IReadOnlyList<Guid> ids, IReadOnlyList<string> columns, CancellationToken cancellationToken)
            => throw new NotSupportedException("FakeSourceRecordService no soporta RetrieveByIdsAsync (no lo usa SubjectGraph).");

        public Task<bool> ExistsAsync(DataReference reference, CancellationToken cancellationToken)
            => throw new NotSupportedException("FakeSourceRecordService no soporta ExistsAsync (no lo usa SubjectGraph).");

        public Task<int> GetApproximateCountAsync(string logicalName, CancellationToken cancellationToken)
            => throw new NotSupportedException("FakeSourceRecordService no soporta GetApproximateCountAsync (no lo usa SubjectGraph).");

        public Task<IReadOnlyList<RecordOperationResult>> WriteBatchAsync(
            string logicalName, IReadOnlyList<DataRecord> batch, WriteStrategy strategy, int pass, CancellationToken cancellationToken)
            => throw new NotSupportedException("FakeSourceRecordService no soporta WriteBatchAsync (SubjectGraph solo lee de Source).");

        public Task AssociateAsync(
            string relationshipSchemaName, DataReference from, IReadOnlyList<DataReference> to, CancellationToken cancellationToken)
            => throw new NotSupportedException("FakeSourceRecordService no soporta AssociateAsync (SubjectGraph solo lee de Source).");

        /// <summary>
        /// A diferencia del resto de los métodos de escritura (no soportados, porque este fake
        /// representa Source), este SÍ tiene una implementación real: lo usan los tests de
        /// <c>SubjectTargetCleaner</c>, que necesitan un <see cref="IDataverseRecordService"/>
        /// "de Target" funcional para verificar el borrado. Idempotente por diseño (igual que pide
        /// el doc-comment de la interfaz): borrar un id que no está en <c>_data</c> también cuenta
        /// como éxito.
        /// </summary>
        public Task<IReadOnlyList<RecordOperationResult>> DeleteBatchAsync(
            string logicalName, IReadOnlyList<Guid> ids, CancellationToken cancellationToken)
        {
            var results = new List<RecordOperationResult>();

            _data.TryGetValue(logicalName, out var list);

            foreach (var id in ids ?? new List<Guid>())
            {
                list?.RemoveAll(r => r.Id == id);
                results.Add(new RecordOperationResult
                {
                    RecordId = id,
                    TableLogicalName = logicalName,
                    Operation = RecordOperation.Delete,
                    Outcome = RecordOutcome.Succeeded
                });
            }

            return Task.FromResult<IReadOnlyList<RecordOperationResult>>(results);
        }

        /// <summary>Expuesto como público y estático para que los tests puedan probar el
        /// resultado de un <see cref="RecordFilter"/> armado por <c>SubjectRelationshipMap</c>
        /// directamente contra un <see cref="DataRecord"/> puntual, sin pasar por una tabla
        /// completa, cuando eso hace el test más legible.</summary>
        public static bool MatchesFilter(DataRecord record, RecordFilter filter)
        {
            if (filter == null || ((filter.Conditions?.Count ?? 0) == 0 && (filter.SubFilters?.Count ?? 0) == 0))
                return true;

            var results = (filter.Conditions ?? new List<FilterCondition>())
                .Select(c => EvaluateCondition(record, c))
                .Concat((filter.SubFilters ?? new List<RecordFilter>()).Select(sf => MatchesFilter(record, sf)))
                .ToList();

            if (results.Count == 0) return true;
            return filter.LogicalOperator == FilterLogicalOperator.Or ? results.Any(x => x) : results.All(x => x);
        }

        private static bool EvaluateCondition(DataRecord record, FilterCondition condition)
        {
            record.Attributes.TryGetValue(condition.AttributeName, out var raw);
            var actual = Normalize(raw);

            if (condition.Operator == FilterOperator.In)
            {
                // Un string implementa IEnumerable<char> — hay que excluirlo explícitamente o un
                // filtro In con un solo valor string se "desarma" en caracteres individuales en
                // vez de tratarse como un único candidato (mismo bug corregido en
                // DataverseRecordServiceAdapter.BuildFilterExpression del proyecto hermano).
                var candidates = (condition.Value is string || !(condition.Value is System.Collections.IEnumerable enumerable))
                    ? new[] { condition.Value }
                    : enumerable.Cast<object>();
                return candidates.Select(Normalize).Any(v => Equals(actual, v));
            }

            var matches = Equals(actual, Normalize(condition.Value));
            return condition.Operator == FilterOperator.NotEqual ? !matches : matches;
        }

        /// <summary>Un lookup real de Dataverse se compara contra un GUID "pelado" (así es como
        /// <c>SubjectRelationshipMap</c> arma sus <see cref="FilterCondition"/>, y así es como
        /// <c>ConditionExpression</c> de Dataverse compara un atributo lookup). Acá los lookups
        /// se guardan como <see cref="DataReference"/> (igual que <c>SubjectResolver</c> los lee
        /// de vuelta con <c>TryGetValue&lt;DataReference&gt;</c>), así que hay que reducirlos a
        /// su <c>.Id</c> antes de comparar.</summary>
        private static object Normalize(object value) => value is DataReference reference ? (object)reference.Id : value;
    }
}
