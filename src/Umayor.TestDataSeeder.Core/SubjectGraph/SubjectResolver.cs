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
    /// Resuelve el <see cref="SubjectContext"/> raíz a partir de un RUT o pasaporte — ver
    /// docs/SUBJECT_RELATIONSHIP_MAP.md, sección "Resolución del contexto raíz". Espejo directo
    /// del bloque 1 ("RECUPERAR CONTACTO UNA SOLA VEZ") del SQL de referencia: mismo criterio de
    /// búsqueda (`wit_rut` o `wit_pasaporte`), mismo desempate (`modifiedon DESC`, el más
    /// reciente primero) si hubiera más de un match.
    /// </summary>
    public static class SubjectResolver
    {
        private static readonly string[] ContactColumns =
        {
            "wit_rut",
            "originatingleadid",
            "wit_caso",
            "wit_eventoorigen",
            "wit_colegio",
            "wit_tramo",
            "wit_ingresobrutofamiliar",
            "modifiedon"
        };

        /// <summary>
        /// Devuelve <c>null</c> si no se encuentra ningún <c>contact</c> con ese RUT/pasaporte —
        /// nunca lanza por "no encontrado", igual que el resto del Core (nunca asumir que un
        /// valor de negocio ausente es un error de programación).
        /// </summary>
        public static async Task<SubjectContext> ResolveAsync(
            IDataverseRecordService sourceRecords,
            string rut,
            string pasaporte,
            CancellationToken cancellationToken)
        {
            if (sourceRecords == null) throw new ArgumentNullException(nameof(sourceRecords));
            if (string.IsNullOrWhiteSpace(rut) && string.IsNullOrWhiteSpace(pasaporte))
                throw new ArgumentException("Hay que indicar RUT o pasaporte (al menos uno).");

            var filter = new RecordFilter { LogicalOperator = FilterLogicalOperator.Or };
            if (!string.IsNullOrWhiteSpace(rut))
                filter.Conditions.Add(new FilterCondition { AttributeName = "wit_rut", Operator = FilterOperator.Equal, Value = NormalizeRut(rut) });
            if (!string.IsNullOrWhiteSpace(pasaporte))
                filter.Conditions.Add(new FilterCondition { AttributeName = "wit_pasaporte", Operator = FilterOperator.Equal, Value = pasaporte.Trim() });

            var candidates = new List<DataRecord>();
            string pageToken = null;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = await sourceRecords
                    .RetrieveFilteredPageAsync("contact", ContactColumns, filter, pageToken, pageSize: 50, cancellationToken)
                    .ConfigureAwait(false);
                candidates.AddRange(page.Records);
                pageToken = page.HasMore ? page.NextPageToken : null;
            }
            while (pageToken != null);

            if (candidates.Count == 0) return null;

            // Mismo desempate que el SQL de referencia ("ORDER BY c.modifiedon DESC" + "TOP 1"):
            // si hay más de un contact con el mismo RUT/pasaporte, se usa el modificado más
            // recientemente. Un contact sin modifiedon (no debería pasar en la práctica) queda
            // al final.
            var chosen = candidates
                .OrderByDescending(r => r.TryGetValue<DateTime>("modifiedon", out var modifiedOn) ? modifiedOn : DateTime.MinValue)
                .First();

            return new SubjectContext
            {
                ContactId = chosen.Id,
                Rut = chosen.TryGetValue<string>("wit_rut", out var storedRut) ? storedRut : null,
                OriginatingLeadId = ReferenceId(chosen, "originatingleadid"),
                WitCaso = ReferenceId(chosen, "wit_caso"),
                WitEventoOrigen = ReferenceId(chosen, "wit_eventoorigen"),
                WitColegio = ReferenceId(chosen, "wit_colegio"),
                WitTramo = ReferenceId(chosen, "wit_tramo"),
                WitIngresoBrutoFamiliar = ReferenceId(chosen, "wit_ingresobrutofamiliar")
            };
        }

        private static Guid? ReferenceId(DataRecord record, string attributeName)
            => record.TryGetValue<DataReference>(attributeName, out var reference) ? reference.Id : (Guid?)null;

        /// <summary>Quita puntos, guiones y espacios, y deja el dígito verificador en mayúscula
        /// ('k' → 'K') — <c>contact.wit_rut</c> en el tenant real de Umayor guarda el RUT
        /// completo (cuerpo + dígito verificador) concatenado sin separador, p. ej. "171752728"
        /// para 17.175.272-8; esto deja que el usuario tipee con o sin puntos/guión en la UI.</summary>
        internal static string NormalizeRut(string rut)
        {
            if (string.IsNullOrWhiteSpace(rut)) return rut;
            var chars = rut.Where(c => c != '.' && c != '-' && !char.IsWhiteSpace(c)).ToArray();
            return new string(chars).ToUpperInvariant();
        }
    }
}
