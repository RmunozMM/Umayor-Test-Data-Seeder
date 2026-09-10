using System;
using System.Collections.Generic;
using DataverseMasterDataMigrator.Core.Abstractions;
using DataverseMasterDataMigrator.Core.Models;

namespace Umayor.TestDataSeeder.Core.Anonymization
{
    /// <summary>
    /// Implementación real de <see cref="IRecordTransformer"/> (el punto de extensión que
    /// <c>DataverseMasterDataMigrator.Core</c> dejó preparado desde Fase 1 — ver
    /// <c>Abstractions/ExtensibilityPorts.cs</c>) — reemplaza los campos identificables de una
    /// persona ANTES de escribirlos en Target. Nunca se conecta con el motor de escritura para
    /// nada más: <c>MigrationExecutor</c> ya llama <c>Transform</c> por cada registro dentro de
    /// <c>SplitForPass1</c>, sin ningún cambio necesario ahí.
    ///
    /// Es una función pura — el mismo valor de entrada (p. ej. el mismo RUT real) siempre
    /// produce el mismo valor de reemplazo, sin ningún mapeo persistido (ver
    /// <see cref="RutGenerator"/>) — así las ~7 tablas de docs/SUBJECT_RELATIONSHIP_MAP.md que
    /// repiten <c>wit_rut</c> quedan anonimizadas de forma consistente entre sí, cada una
    /// evaluada de forma independiente.
    ///
    /// Deliberadamente NO expone forma de desactivarse — ver UI (Phase 2): no hay checkbox
    /// "no anonimizar". Si algún día hace falta un modo sin anonimizar (p. ej. para depurar el
    /// propio mapa de relaciones contra Preview Data, sin escribir nada), debe ser una decisión
    /// explícita tomada aparte, no un flag de esta clase.
    /// </summary>
    public sealed class SubjectAnonymizingTransformer : IRecordTransformer
    {
        public DataRecord Transform(DataRecord source, TableSummary tableMetadata)
        {
            if (source == null) return null;

            DataRecord clone = null;
            void EnsureClone()
            {
                if (clone != null) return;
                clone = new DataRecord(source.LogicalName, source.Id);
                foreach (var kvp in source.Attributes) clone.Attributes[kvp.Key] = kvp.Value;
            }

            void Set(string attribute, object value)
            {
                if (!source.Attributes.ContainsKey(attribute)) return;
                EnsureClone();
                clone.Attributes[attribute] = value;
            }

            if (string.Equals(source.LogicalName, "contact", StringComparison.OrdinalIgnoreCase))
            {
                AnonymizeContact(source, Set);
            }
            else if (SecondaryRutTables.Contains(source.LogicalName))
            {
                AnonymizeSecondaryRut(source, Set);
            }

            return clone ?? source;
        }

        /// <summary>Tablas de docs/SUBJECT_RELATIONSHIP_MAP.md cuyo único campo PII directo es un
        /// <c>wit_rut</c> denormalizado (sin <c>wit_dv</c> separado, a diferencia de contact).</summary>
        private static readonly HashSet<string> SecondaryRutTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "phonecall",
            "wit_actividadchat",
            "wit_solicituddeadmisiondirecta",
            "wit_historicodatosdecontactabilidad",
            "wit_contactodelsegmentocomercial",
        };

        private static void AnonymizeContact(DataRecord source, Action<string, object> set)
        {
            var seed = source.Id.ToString();

            if (source.TryGetValue<string>("wit_rut", out var realRut) && !string.IsNullOrWhiteSpace(realRut))
            {
                var fake = RutGenerator.Generate(realRut);
                set("wit_rut", fake.Body);
                set("wit_dv", fake.CheckDigit.ToString());
            }

            if (source.TryGetValue<string>("wit_pasaporte", out var realPassport) && !string.IsNullOrWhiteSpace(realPassport))
            {
                set("wit_pasaporte", PiiFaker.Passport(realPassport));
            }

            set("firstname", PiiFaker.Name("Nombre", seed + ":firstname"));
            set("lastname", PiiFaker.Name("ApellidoPaterno", seed + ":lastname"));
            set("wit_apellidomaterno", PiiFaker.Name("ApellidoMaterno", seed + ":apellidomaterno"));
            set("middlename", PiiFaker.Name("SegundoNombre", seed + ":middlename"));
            set("fullname", PiiFaker.Name("Contacto", seed + ":fullname"));
            set("nickname", PiiFaker.Name("Apodo", seed + ":nickname"));
            set("wit_nombresocial", PiiFaker.Name("NombreSocial", seed + ":nombresocial"));
            set("spousesname", PiiFaker.Name("Conyuge", seed + ":spouse"));
            set("childrensnames", PiiFaker.Name("Hijos", seed + ":children"));
            set("yomifirstname", PiiFaker.Name("Nombre", seed + ":yomifirstname"));
            set("yomilastname", PiiFaker.Name("ApellidoPaterno", seed + ":yomilastname"));
            set("yomimiddlename", PiiFaker.Name("SegundoNombre", seed + ":yomimiddlename"));
            set("yomifullname", PiiFaker.Name("Contacto", seed + ":yomifullname"));

            set("birthdate", PiiFaker.BirthDate(seed + ":birthdate"));
            set("wit_fechadenacimiento", PiiFaker.BirthDate(seed + ":wit_fechadenacimiento"));

            set("emailaddress1", PiiFaker.Email(seed + ":email1"));
            set("emailaddress2", PiiFaker.Email(seed + ":email2"));
            set("emailaddress3", PiiFaker.Email(seed + ":email3"));

            set("telephone1", PiiFaker.Phone(seed + ":tel1"));
            set("telephone2", PiiFaker.Phone(seed + ":tel2"));
            set("telephone3", PiiFaker.Phone(seed + ":tel3"));
            set("mobilephone", PiiFaker.Phone(seed + ":mobile"));
            set("assistantphone", PiiFaker.Phone(seed + ":assistant"));
            set("managerphone", PiiFaker.Phone(seed + ":manager"));

            foreach (var prefix in new[] { "address1_", "address2_", "address3_" })
            {
                set(prefix + "line1", PiiFaker.AddressLine);
                set(prefix + "line2", string.Empty);
                set(prefix + "line3", string.Empty);
                set(prefix + "city", PiiFaker.City);
                set(prefix + "postalcode", PiiFaker.PostalCode);
                set(prefix + "primarycontactname", PiiFaker.Name("Contacto", seed + ":" + prefix));
            }
        }

        private static void AnonymizeSecondaryRut(DataRecord source, Action<string, object> set)
        {
            if (!source.TryGetValue<string>("wit_rut", out var realRut) || string.IsNullOrWhiteSpace(realRut)) return;
            set("wit_rut", RutGenerator.Generate(realRut).Formatted);
        }
    }
}
