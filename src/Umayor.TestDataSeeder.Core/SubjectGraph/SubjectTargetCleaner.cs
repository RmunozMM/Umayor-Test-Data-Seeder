using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataverseMasterDataMigrator.Core.Abstractions;
using DataverseMasterDataMigrator.Core.Migration;
using DataverseMasterDataMigrator.Core.Models;
using DataverseMasterDataMigrator.Core.Planning;

namespace Umayor.TestDataSeeder.Core.SubjectGraph
{
    public sealed class TableCleanupResult
    {
        public string LogicalName { get; set; }
        public int Deleted { get; set; }
        public int Failed { get; set; }
        public List<string> SampleErrors { get; set; } = new List<string>();
    }

    /// <summary>
    /// Borra en TARGET (nunca en Source) todos los registros de un sujeto ya resuelto, en orden
    /// INVERSO al de creación (los dependientes primero — p. ej. activitymimeattachment antes que
    /// email — para no romper FKs), para poder re-testear un sujeto desde cero. Reutiliza
    /// MigrationPlanner (el mismo orden topológico que ya usa la migración real) en vez de
    /// inventar uno propio.
    /// </summary>
    public static class SubjectTargetCleaner
    {
        public static async Task<IReadOnlyList<TableCleanupResult>> DeleteFromTargetAsync(
            MigrationProfile profile,
            IReadOnlyDictionary<string, TableSummary> targetTables,
            IDataverseRecordService targetRecords,
            Action<string> onProgress,
            CancellationToken cancellationToken)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (targetTables == null) throw new ArgumentNullException(nameof(targetTables));
            if (targetRecords == null) throw new ArgumentNullException(nameof(targetRecords));

            var plan = MigrationPlanner.CreatePlan(profile, targetTables);
            var results = new List<TableCleanupResult>();

            if (plan.HasCycles)
            {
                var cycleNames = string.Join(" | ", plan.Cycles.Select(c => string.Join(" -> ", c)));
                onProgress?.Invoke($"ADVERTENCIA: el plan de borrado tiene {plan.Cycles.Count} ciclo(s) de dependencias ({cycleNames}) — el orden entre esas tablas no está garantizado. No se bloquea el borrado, pero revisá el resultado si alguna falla por FK.");
            }

            // Orden inverso al de creación: lo último que se creó (más dependiente) se borra primero.
            foreach (var step in plan.Steps.Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();

                var entityConfig = profile.Entities.FirstOrDefault(e =>
                    string.Equals(e.LogicalName, step.LogicalName, StringComparison.OrdinalIgnoreCase));
                if (entityConfig == null || !targetTables.TryGetValue(step.LogicalName, out var table)) continue;

                // GUARDA DEFENSIVA (defensa en profundidad — no confiar solo en la garantía de
                // SubjectProfileBuilder.BuildAsync, que hoy sí sustituye todo Filter vacío por un
                // NoMatchFilter antes de agregar la entidad al perfil): RecordFilter define
                // contractualmente que un filtro nulo o sin Conditions/SubFilters significa "sin
                // filtro, toda la tabla" (ver DataverseRecordServiceAdapter.RetrieveFilteredPageAsync
                // y IDataverseRecordService.RetrieveFilteredPageAsync). Para este borrador NUNCA es
                // aceptable ese significado — un filtro vacío acá borraría la tabla ENTERA de
                // Target, no solo el sujeto. Si llegara a pasar (perfil armado a mano, regresión
                // futura en el guard de SubjectProfileBuilder, etc.), esta tabla se SALTEA por
                // completo antes de cualquier llamada de red — nunca "confiar y seguir".
                if (IsEmptyFilter(entityConfig.Filter))
                {
                    onProgress?.Invoke($"'{step.LogicalName}': filtro vacío o ausente — SALTEADA por seguridad, no se borra nada.");
                    results.Add(new TableCleanupResult { LogicalName = step.LogicalName, Deleted = 0, Failed = 0 });
                    continue;
                }

                onProgress?.Invoke($"Buscando registros de '{step.LogicalName}' en Target...");

                var ids = new List<Guid>();
                string pageToken = null;
                do
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var page = await targetRecords.RetrieveFilteredPageAsync(
                        step.LogicalName, new List<string> { table.PrimaryIdAttribute }, entityConfig.Filter, pageToken, 500, cancellationToken)
                        .ConfigureAwait(false);
                    pageToken = page.HasMore ? page.NextPageToken : null;
                    ids.AddRange(page.Records.Select(r => r.Id));
                }
                while (pageToken != null);

                if (ids.Count == 0)
                {
                    results.Add(new TableCleanupResult { LogicalName = step.LogicalName, Deleted = 0, Failed = 0 });
                    continue;
                }

                onProgress?.Invoke($"Eliminando {ids.Count} registro(s) de '{step.LogicalName}' en Target...");

                var tableResult = new TableCleanupResult { LogicalName = step.LogicalName };
                const int chunkSize = 100;
                for (int i = 0; i < ids.Count; i += chunkSize)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var chunk = ids.Skip(i).Take(chunkSize).ToList();
                    var opResults = await targetRecords.DeleteBatchAsync(step.LogicalName, chunk, cancellationToken).ConfigureAwait(false);

                    foreach (var r in opResults)
                    {
                        if (r.Outcome == RecordOutcome.Succeeded) tableResult.Deleted++;
                        else
                        {
                            tableResult.Failed++;
                            if (tableResult.SampleErrors.Count < 5 && !string.IsNullOrEmpty(r.ErrorMessage))
                                tableResult.SampleErrors.Add(r.ErrorMessage);
                        }
                    }
                }

                results.Add(tableResult);
            }

            return results;
        }

        /// <summary>Réplica deliberada de <c>SubjectProfileBuilder.IsEmpty</c> — misma semántica
        /// exacta ("sin Conditions ni SubFilters" == vacío), pero como método propio de esta
        /// clase a propósito: esta guarda tiene que sobrevivir aunque el guard de
        /// SubjectProfileBuilder algún día se rompa o se bypasee, no depender de un helper
        /// compartido con él.</summary>
        private static bool IsEmptyFilter(RecordFilter filter)
            => filter == null || ((filter.Conditions?.Count ?? 0) == 0 && (filter.SubFilters?.Count ?? 0) == 0);
    }
}
