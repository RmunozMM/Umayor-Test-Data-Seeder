using System;
using DataverseMasterDataMigrator.Core.Models;
using Umayor.TestDataSeeder.Core.Anonymization;
using Xunit;

namespace Umayor.TestDataSeeder.Tests.Anonymization
{
    /// <summary>
    /// Verificación independiente de <see cref="SubjectAnonymizingTransformer"/>: que el RUT
    /// resultante sea realmente válido (no solo "distinto"), que nunca invente atributos que
    /// Dataverse no pidió escribir, que la consistencia entre <c>contact</c> y las tablas
    /// secundarias con <c>wit_rut</c> denormalizado se sostenga en la práctica (no solo en el
    /// diseño), y que tablas fuera del alcance de PII queden intactas.
    /// </summary>
    public class SubjectAnonymizingTransformerTests
    {
        private static readonly SubjectAnonymizingTransformer Transformer = new SubjectAnonymizingTransformer();

        [Fact]
        public void Transform_Contact_ProducesValidSyntheticRut()
        {
            var contact = new DataRecord("contact", Guid.NewGuid());
            contact.Attributes["wit_rut"] = "12345678";
            contact.Attributes["wit_dv"] = "5";

            var result = Transformer.Transform(contact, null);

            var resultRut = (string)result.Attributes["wit_rut"];
            var resultDv = (string)result.Attributes["wit_dv"];

            Assert.NotEqual("12345678", resultRut);
            Assert.NotEqual("5", resultDv);

            var expectedCheckDigit = RutGenerator.ComputeCheckDigit(int.Parse(resultRut)).ToString();
            Assert.Equal(expectedCheckDigit, resultDv);
        }

        [Fact]
        public void Transform_Contact_DoesNotInventAttributesNeverPresentOnSource()
        {
            var contact = new DataRecord("contact", Guid.NewGuid());
            contact.Attributes["wit_rut"] = "12345678";
            contact.Attributes["wit_dv"] = "5";
            // firstname deliberadamente NUNCA seteado — Dataverse nunca pidió escribirlo.

            var result = Transformer.Transform(contact, null);

            Assert.False(result.Attributes.ContainsKey("firstname"),
                "Transform no debe agregar atributos que el DataRecord de origen nunca tuvo.");
        }

        [Fact]
        public void Transform_ContactAndSecondaryRutTable_ProduceConsistentSyntheticRutForSameRealRut()
        {
            const string realRut = "11.222.333-4";

            var contact = new DataRecord("contact", Guid.NewGuid());
            contact.Attributes["wit_rut"] = realRut;
            contact.Attributes["wit_dv"] = "placeholder";

            var phonecall = new DataRecord("phonecall", Guid.NewGuid());
            phonecall.Attributes["wit_rut"] = realRut;

            var contactResult = Transformer.Transform(contact, null);
            var phonecallResult = Transformer.Transform(phonecall, null);

            var contactFakeRut = (string)contactResult.Attributes["wit_rut"];
            var contactFakeDv = (string)contactResult.Attributes["wit_dv"];
            var phonecallFakeRut = (string)phonecallResult.Attributes["wit_rut"];

            Assert.Equal($"{contactFakeRut}-{contactFakeDv}", phonecallFakeRut);
        }

        [Theory]
        [InlineData("wit_actividadchat")]
        [InlineData("wit_solicituddeadmisiondirecta")]
        [InlineData("wit_historicodatosdecontactabilidad")]
        [InlineData("wit_contactodelsegmentocomercial")]
        public void Transform_OtherSecondaryRutTables_AlsoStayConsistentWithContact(string secondaryTable)
        {
            const string realRut = "9.876.543-2";

            var contact = new DataRecord("contact", Guid.NewGuid());
            contact.Attributes["wit_rut"] = realRut;
            contact.Attributes["wit_dv"] = "placeholder";

            var secondary = new DataRecord(secondaryTable, Guid.NewGuid());
            secondary.Attributes["wit_rut"] = realRut;

            var contactResult = Transformer.Transform(contact, null);
            var secondaryResult = Transformer.Transform(secondary, null);

            var expectedFormatted = $"{contactResult.Attributes["wit_rut"]}-{contactResult.Attributes["wit_dv"]}";
            Assert.Equal(expectedFormatted, secondaryResult.Attributes["wit_rut"]);
        }

        [Fact]
        public void Transform_TableWithoutRut_ReturnsSourceRecordUnchanged()
        {
            var record = new DataRecord("wit_visitaweb", Guid.NewGuid());
            record.Attributes["wit_url"] = "https://example.org/page";
            record.Attributes["createdon"] = new DateTime(2026, 1, 1);

            var result = Transformer.Transform(record, null);

            Assert.Same(record, result);
            Assert.Equal("https://example.org/page", result.Attributes["wit_url"]);
        }

        [Fact]
        public void Transform_Contact_EmptyAttributes_DoesNotThrow()
        {
            var contact = new DataRecord("contact", Guid.NewGuid());

            var exception = Record.Exception(() => Transformer.Transform(contact, null));

            Assert.Null(exception);
        }

        [Fact]
        public void Transform_SecondaryRutTable_EmptyAttributes_DoesNotThrow()
        {
            var phonecall = new DataRecord("phonecall", Guid.NewGuid());

            var exception = Record.Exception(() => Transformer.Transform(phonecall, null));

            Assert.Null(exception);
        }

        [Fact]
        public void Transform_NullSource_ReturnsNull()
        {
            var result = Transformer.Transform(null, null);

            Assert.Null(result);
        }
    }
}
