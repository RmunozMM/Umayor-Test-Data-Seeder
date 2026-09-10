using System;
using System.Security.Cryptography;
using System.Text;

namespace Umayor.TestDataSeeder.Core.Anonymization
{
    /// <summary>
    /// Generadores determinísticos de valores de reemplazo para PII que no es el RUT (ver
    /// <see cref="RutGenerator"/> para ese caso). Deliberadamente NO intenta generar nombres
    /// "realistas" — usa placeholders obviamente sintéticos ("Nombre Prueba 00042") para que
    /// nadie pueda confundir un registro anonimizado con uno real al mirarlo, y para evitar el
    /// riesgo (aunque remoto) de que un nombre "realista" generado al azar coincida con el de una
    /// persona real. El sufijo numérico es determinístico (mismo valor de entrada → mismo
    /// sufijo siempre) para poder distinguir registros distintos sin exponer nada real.
    /// </summary>
    public static class PiiFaker
    {
        public static string Name(string label, string seed) => $"{label} Prueba {ShortSuffix(seed)}";

        public static string Email(string seed) => $"sujeto{ShortSuffix(seed)}@datos-prueba.umayor.local";

        public static string Phone(string seed) => "+569" + (StableHash(seed) % 100_000_000).ToString("D8");

        public static string Passport(string seed) => "P" + (StableHash(seed) % 100_000_000).ToString("D8");

        public static DateTime BirthDate(string seed)
        {
            var hash = StableHash(seed);
            var year = 1975 + (int)(hash % 35); // 1975-2009: rango plausible de edad para un postulante/estudiante
            var month = 1 + (int)((hash / 35) % 12);
            var day = 1 + (int)((hash / (35 * 12)) % 28); // 28 evita meses cortos/años bisiestos
            return new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
        }

        public const string AddressLine = "Dirección de prueba";
        public const string City = "Ciudad de prueba";
        public const string PostalCode = "0000000";

        private static string ShortSuffix(string seed)
            => (StableHash(seed) % 100_000).ToString("D5");

        private static uint StableHash(string value)
        {
            if (value == null) value = string.Empty;
            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
                return BitConverter.ToUInt32(bytes, 0);
            }
        }
    }
}
