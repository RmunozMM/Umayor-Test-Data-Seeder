using System;

namespace Umayor.TestDataSeeder.Core.SubjectGraph
{
    /// <summary>
    /// Resultado de resolver un sujeto (RUT o pasaporte) contra <c>contact</c> — ver
    /// docs/SUBJECT_RELATIONSHIP_MAP.md, sección "Resolución del contexto raíz". Todos los campos
    /// salvo <see cref="ContactId"/> pueden ser <c>null</c> (el contacto puede no tener ese
    /// lookup seteado); una condición sobre un campo <c>null</c> nunca se agrega a ningún filtro.
    /// </summary>
    public sealed class SubjectContext
    {
        public Guid ContactId { get; set; }

        /// <summary>El RUT tal como quedó guardado en <c>contact.wit_rut</c> del registro
        /// resuelto (no necesariamente el mismo string que se usó para buscar, si se buscó por
        /// pasaporte). Puede ser <c>null</c> si el contacto no tiene RUT cargado. Varias tablas
        /// del mapa (ver docs/SUBJECT_RELATIONSHIP_MAP.md) tienen su propia columna
        /// <c>wit_rut</c> denormalizada y se buscan también por este valor, no solo por
        /// <see cref="ContactId"/>.</summary>
        public string Rut { get; set; }

        public Guid? OriginatingLeadId { get; set; }
        public Guid? WitCaso { get; set; }
        public Guid? WitEventoOrigen { get; set; }
        public Guid? WitColegio { get; set; }
        public Guid? WitTramo { get; set; }
        public Guid? WitIngresoBrutoFamiliar { get; set; }
    }
}
