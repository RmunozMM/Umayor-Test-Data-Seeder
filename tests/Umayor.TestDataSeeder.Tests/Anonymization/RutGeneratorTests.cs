using System.Collections.Generic;
using Umayor.TestDataSeeder.Core.Anonymization;
using Xunit;

namespace Umayor.TestDataSeeder.Tests.Anonymization
{
    /// <summary>
    /// Verificación independiente del algoritmo módulo 11 estándar chileno usado por
    /// <see cref="RutGenerator"/>. Los casos base (12345678, 11111111, 7654321) fueron
    /// calculados a mano con el algoritmo de referencia (pesos 2..7 cíclicos de derecha a
    /// izquierda, resto de dividir por 11, 11→'0', 10→'K') antes de comparar contra el
    /// método real. Los casos de "40" y "14" se construyeron a propósito iterando cuerpos
    /// simples hasta encontrar uno que caiga en cada rama especial:
    ///   - "40": suma=12 → 12 % 11 = 1 → resto = 11-1 = 10 → 'K'.
    ///   - "14": suma=11 → 11 % 11 = 0 → resto = 11-0 = 11 → '0'.
    /// </summary>
    public class RutGeneratorTests
    {
        [Theory]
        [InlineData(12345678, '5')] // caso público de referencia, DV esperado documentado por el PM
        [InlineData(11111111, '1')]
        [InlineData(7654321, '6')]
        [InlineData(40, 'K')] // rama resto==10
        [InlineData(14, '0')] // rama resto==11
        public void ComputeCheckDigit_KnownBodies_MatchesStandardModulo11Algorithm(int body, char expectedCheckDigit)
        {
            var actual = RutGenerator.ComputeCheckDigit(body);

            Assert.Equal(expectedCheckDigit, actual);
        }

        [Fact]
        public void Generate_SameSeed_IsDeterministic()
        {
            var first = RutGenerator.Generate("12.345.678-9");
            var second = RutGenerator.Generate("12.345.678-9");

            Assert.Equal(first.Body, second.Body);
            Assert.Equal(first.CheckDigit, second.CheckDigit);
        }

        [Theory]
        [InlineData("12.345.678-9")]
        [InlineData("7.654.321-6")]
        [InlineData("sin-rut")]
        [InlineData("")]
        [InlineData(null)]
        public void Generate_CheckDigit_IsAlwaysConsistentWithComputeCheckDigit(string seed)
        {
            // Generate no debe inventar un dígito verificador aparte: el que devuelve tiene
            // que ser exactamente ComputeCheckDigit(Body), el mismo algoritmo real.
            var fake = RutGenerator.Generate(seed);

            var expectedCheckDigit = RutGenerator.ComputeCheckDigit(int.Parse(fake.Body));

            Assert.Equal(expectedCheckDigit, fake.CheckDigit);
        }

        [Fact]
        public void Generate_DifferentSeeds_TypicallyProduceDifferentBodies()
        {
            // No es una garantía matemática (hash de 32 bits reducido a un rango de 3M), pero
            // para un puñado de seeds distintos no deberíamos ver colisiones triviales.
            var seeds = new[] { "11.111.111-1", "22.222.222-2", "33.333.333-3", "44.444.444-4" };
            var bodies = new HashSet<string>();

            foreach (var seed in seeds)
            {
                bodies.Add(RutGenerator.Generate(seed).Body);
            }

            Assert.Equal(seeds.Length, bodies.Count);
        }

        [Fact]
        public void Generate_BodyIsWithinDocumentedSyntheticRange()
        {
            var fake = RutGenerator.Generate("12.345.678-9");
            var bodyValue = int.Parse(fake.Body);

            Assert.InRange(bodyValue, 1_000_000, 3_999_999);
        }

        [Fact]
        public void SyntheticRut_Formatted_CombinesBodyAndCheckDigitWithDash()
        {
            var fake = RutGenerator.Generate("12.345.678-9");

            Assert.Equal($"{fake.Body}-{fake.CheckDigit}", fake.Formatted);
        }
    }
}
