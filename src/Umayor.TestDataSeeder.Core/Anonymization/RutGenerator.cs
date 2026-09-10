using System;
using System.Security.Cryptography;
using System.Text;

namespace Umayor.TestDataSeeder.Core.Anonymization
{
    /// <summary>
    /// Genera un RUT chileno sintético, determinístico a partir de un valor de entrada (el RUT
    /// real) — el mismo RUT real siempre produce el mismo RUT falso, sin necesitar ningún mapeo
    /// persistido: la consistencia entre las ~7 tablas que repiten <c>wit_rut</c> (ver
    /// docs/SUBJECT_RELATIONSHIP_MAP.md) sale gratis de que cada una se anonimiza de forma
    /// independiente con la misma función pura.
    /// No pretende evitar colisiones contra RUTs reales existentes (no consulta ningún padrón) —
    /// el objetivo es dato de prueba con formato válido, no una garantía de no-colisión.
    /// </summary>
    /// <summary>Un RUT sintético en sus dos representaciones — algunas tablas guardan el cuerpo y
    /// el dígito verificador en columnas separadas (<c>contact.wit_rut</c>/<c>contact.wit_dv</c>),
    /// otras lo denormalizan como un único string formateado.</summary>
    public struct SyntheticRut
    {
        public string Body { get; set; }
        public char CheckDigit { get; set; }
        public string Formatted => $"{Body}-{CheckDigit}";
    }

    public static class RutGenerator
    {
        // Rango reservado arbitrariamente para RUTs sintéticos: por debajo del piso real de
        // RUTs de persona natural vigentes (que hoy parten sobre los 5.000.000), para que un
        // RUT anonimizado por esta herramienta sea reconocible como tal a simple vista.
        private const int RangeStart = 1_000_000;
        private const int RangeSize = 3_000_000; // cubre 1.000.000-3.999.999

        public static SyntheticRut Generate(string seed)
        {
            if (string.IsNullOrWhiteSpace(seed)) seed = "sin-rut";

            var body = RangeStart + (int)(StableHash(seed) % RangeSize);
            return new SyntheticRut { Body = body.ToString(), CheckDigit = ComputeCheckDigit(body) };
        }

        /// <summary>Dígito verificador módulo 11 estándar chileno: pesos 2..7 cíclicos de derecha
        /// a izquierda; resto 11 → '0', resto 10 → 'K'.</summary>
        public static char ComputeCheckDigit(int rutBody)
        {
            var digits = rutBody.ToString();
            int sum = 0;
            int weight = 2;
            for (int i = digits.Length - 1; i >= 0; i--)
            {
                sum += (digits[i] - '0') * weight;
                weight = weight == 7 ? 2 : weight + 1;
            }

            var remainder = 11 - (sum % 11);
            if (remainder == 11) return '0';
            if (remainder == 10) return 'K';
            return (char)('0' + remainder);
        }

        /// <summary>Hash estable entre ejecuciones/máquinas (a diferencia de <see cref="string.GetHashCode"/>,
        /// que varía entre procesos por diseño desde .NET Core — acá igual estamos en .NET
        /// Framework donde es estable, pero se usa SHA256 explícito para no depender de ese
        /// detalle de implementación).</summary>
        private static uint StableHash(string value)
        {
            using (var sha256 = SHA256.Create())
            {
                var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
                return BitConverter.ToUInt32(bytes, 0);
            }
        }
    }
}
