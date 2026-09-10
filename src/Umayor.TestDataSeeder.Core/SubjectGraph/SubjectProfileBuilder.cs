using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DataverseMasterDataMigrator.Core.Abstractions;
using DataverseMasterDataMigrator.Core.Models;

namespace Umayor.TestDataSeeder.Core.SubjectGraph
{
    public sealed class SubjectProfileResult
    {
        public SubjectContext Context { get; set; }
        public MigrationProfile Profile { get; set; }
    }

    /// <summary>
    /// Orquesta <see cref="SubjectResolver"/> + <see cref="SubjectRelationshipMap"/> para producir
    /// un <see cref="MigrationProfile"/> real (en memoria, no persistido como JSON de perfil
    /// normal) listo para <c>MigrationPlanner</c>/<c>MigrationExecutor</c> del Core compartido —
    /// cada <see cref="ProfileEntity.Filter"/> queda resuelto con los GUIDs reales del sujeto.
    /// Ningún cambio al motor de escritura de <c>DataverseMasterDataMigrator.Core</c>: esta clase
    /// solo arma el "qué migrar", el resto (Pass 1/2/3, retry, manifest) se reutiliza tal cual.
    /// </summary>
    public static class SubjectProfileBuilder
    {
        public static async Task<SubjectProfileResult> BuildAsync(
            IDataverseRecordService sourceRecords,
            string rut,
            string pasaporte,
            CancellationToken cancellationToken)
        {
            var context = await SubjectResolver.ResolveAsync(sourceRecords, rut, pasaporte, cancellationToken).ConfigureAwait(false);
            if (context == null) return null;

            var profile = new MigrationProfile
            {
                Name = $"Sujeto {rut ?? pasaporte}",
                Description = "Perfil generado en memoria por SubjectProfileBuilder — no persistido como JSON de perfil normal.",
                Options = new ProfileOptions
                {
                    // Bug real encontrado migrando contra el tenant real: un lookup externo al
                    // perfil (p. ej. contact.wit_resultadoultimocorreoelectronico_detalle ->
                    // wit_detalledeactividad, una tabla fuera de las 28 del mapa) con un GUID que
                    // no existe en Target tumbaba la escritura de "contact" entero — y como
                    // "contact" nunca se creaba, TODO lo que depende de él fallaba en cascada
                    // (336 de 337 registros de una corrida real). A diferencia del migrador
                    // genérico (donde saltear un lookup obligatorio es una decisión delicada que
                    // se dejó fuera de alcance a propósito), acá la alternativa a SkipSilently es
                    // "el sujeto completo no se migra" — mucho peor para el propósito real de
                    // esta herramienta (tener datos de prueba anonimizados, no una réplica 100%
                    // fiel). Sin UI para elegir esto — es la política por defecto y única.
                    RequiredLookupPolicy = LookupPolicy.SkipSilently,
                    OptionalLookupPolicy = LookupPolicy.SkipSilently
                }
            };

            int order = 0;
            foreach (var rule in SubjectRelationshipMap.Rules)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var filter = await rule.BuildFilterAsync(context, sourceRecords, cancellationToken).ConfigureAwait(false);

                // RecordFilter (DataverseMasterDataMigrator.Core.Models) define contractualmente
                // que un filtro vacío (sin Conditions ni SubFilters) significa "sin filtro, todos
                // los registros" — el comportamiento correcto para el migrador genérico. Para
                // este selector NUNCA es correcto: si una regla no logró resolver ninguna
                // condición (p. ej. todos los GUIDs de contexto de los que depende son null, o
                // una resolución de 2 saltos no encontró IDs intermedios), el resultado debe ser
                // "ningún registro de esta tabla", no "toda la tabla" — lo segundo filtraría
                // datos de CUALQUIER otro sujeto hacia el entorno bajo, exactamente lo que esta
                // herramienta existe para evitar. Guard único acá, no repetido regla por regla,
                // para que ninguna regla nueva pueda reintroducir el mismo bug por omisión.
                if (IsEmpty(filter))
                {
                    filter = NoMatchFilter(rule.LogicalName);
                }

                profile.Entities.Add(new ProfileEntity
                {
                    LogicalName = rule.LogicalName,
                    DisplayName = rule.LogicalName,
                    // Bug real: "The 'Create' method does not support entities of type
                    // 'activitypointer'" / "...'activityparty'" — ninguna de las dos se puede
                    // crear directamente en Dataverse. activitypointer es la vista base
                    // polimórfica de CUALQUIER actividad (el dato real vive en la entidad
                    // concreta — email, phonecall, etc. — que este mismo mapa ya migra por
                    // separado); activityparty se crea implícitamente al setear los campos
                    // to/from/requiredattendees de una actividad, nunca vía Create genérico.
                    // Ambas quedan en el mapa SOLO como paso de resolución de IDs (ver
                    // SubjectRelationshipMap — sus queries van directo contra
                    // IDataverseRecordService, no dependen de este Enabled), nunca como datos a
                    // escribir.
                    Enabled = !NonWritableResolutionOnlyTables.Contains(rule.LogicalName),
                    PreferredOrder = order++,
                    Filter = filter
                });
            }

            return new SubjectProfileResult { Context = context, Profile = profile };
        }

        /// <summary>Tablas de docs/SUBJECT_RELATIONSHIP_MAP.md que Dataverse no deja crear
        /// directamente — quedan en el perfil con <c>Enabled = false</c> (nunca se escriben ni
        /// se cuentan en Preview), pero <see cref="SubjectRelationshipMap"/> sigue pudiendo
        /// usarlas para resolver IDs de otras filas.</summary>
        private static readonly HashSet<string> NonWritableResolutionOnlyTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "activitypointer",
            "activityparty",
        };

        private static bool IsEmpty(RecordFilter filter)
            => filter == null || ((filter.Conditions?.Count ?? 0) == 0 && (filter.SubFilters?.Count ?? 0) == 0);

        /// <summary>Tablas del mapa de relaciones (docs/SUBJECT_RELATIONSHIP_MAP.md) que son de
        /// tipo Activity en Dataverse — su primary key real es SIEMPRE <c>activityid</c>, nunca
        /// <c>&lt;logicalname&gt;id</c> (bug real encontrado en vivo: "'ActivityPointer' entity
        /// doesn't contain attribute with Name = 'activitypointerid'" al intentar filtrar
        /// <c>activitypointer</c> — la fila 26 del mapa, la más propensa a caer en
        /// <see cref="NoMatchFilter"/> porque solo tiene una vía de resolución, la de
        /// <c>activityparty</c>). <c>activityparty</c> mismo NO es Activity-type — su propia
        /// primary key es <c>activitypartyid</c>, la convención estándar aplica ahí sin problema.</summary>
        private static readonly HashSet<string> ActivityTypeTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "activitypointer",
            "email",
            "phonecall",
            "wit_actividadchat",
            "wit_visitaweb",
            "wit_visitapresencial",
            "wit_evento",
            "wit_whatsapp",
            "wit_sms",
            "msdyn_ocliveworkitem",
        };

        /// <summary>Un filtro que nunca matchea ningún registro real — comparar la primary key
        /// real contra <see cref="Guid.Empty"/>, que Dataverse nunca asigna a un registro real.</summary>
        private static RecordFilter NoMatchFilter(string logicalName)
        {
            var primaryIdAttribute = ActivityTypeTables.Contains(logicalName) ? "activityid" : logicalName + "id";
            return new RecordFilter
            {
                LogicalOperator = FilterLogicalOperator.And,
                Conditions = new List<FilterCondition>
                {
                    new FilterCondition { AttributeName = primaryIdAttribute, Operator = FilterOperator.Equal, Value = Guid.Empty }
                }
            };
        }
    }
}
