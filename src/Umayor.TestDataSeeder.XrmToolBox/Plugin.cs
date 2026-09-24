using System;
using System.ComponentModel.Composition;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using XrmToolBox.Extensibility;
using XrmToolBox.Extensibility.Interfaces;
using Umayor.TestDataSeeder.XrmToolBox.UI;

namespace Umayor.TestDataSeeder.XrmToolBox
{
    // Iconos: 32x32 (SmallImageBase64) y 80x80 (BigImageBase64), los mismos tamaños que usan
    // DataverseMasterDataMigrator y Metadata Dataverse Document. PNG fuente en Resources/
    // (icon_small_32.png, icon_big_80.png); se regeneran con tools/make_icons.py, que además
    // imprime el base64 y su largo. El ícono NO usa el escudo de la Universidad Mayor: el Manual
    // de Marca UMayor 2024 prohíbe separar el escudo del texto del isologo, y el isologo completo
    // es ilegible a 32 px — por eso es un ícono propio con la paleta de marca (Gris 2 #343742,
    // Amarillo #FECE40, blanco). Volver a correr tools/verify_attr_blobs.py del repo hermano
    // contra el DLL cada vez que cambien: cualquier string de ExportMetadata sobre 16383
    // caracteres arriesga la CustomAttributeFormatException que ya tumbó a Metadata Dataverse
    // Document.
    [Export(typeof(IXrmToolBoxPlugin)),
        ExportMetadata("Name", "Umayor Test Data Seeder"),
        ExportMetadata("Description", "Extrae el grafo de registros de un RUT/pasaporte desde Producción, lo anonimiza y lo migra a un entorno bajo."),
        ExportMetadata("PluginType", "DataMigration"),
        ExportMetadata("SmallImageBase64", "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"),
        ExportMetadata("BigImageBase64", "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"),
        ExportMetadata("BackgroundColor", "WhiteSmoke"),
        ExportMetadata("PrimaryFontColor", "Black"),
        ExportMetadata("SecondaryFontColor", "Gray")]
    public class Plugin : PluginBase
    {
        // Mismo mecanismo de aislamiento de dependencias que DataverseMasterDataMigrator.Plugin —
        // ver ese archivo para el razonamiento completo. Este tool hoy no tiene dependencias
        // propias con desajuste de versión conocido (no usa ClosedXML), así que no hace falta la
        // lista de redirección forzada — solo el resolver general por subcarpeta propia.
        private static int _resolverRegistered;

        public Plugin()
        {
            if (Interlocked.Exchange(ref _resolverRegistered, 1) == 0)
            {
                AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolveEventHandler;
            }
        }

        public override IXrmToolBoxPluginControl GetControl()
        {
            return new PluginControl();
        }

        private static Assembly AssemblyResolveEventHandler(object sender, ResolveEventArgs args)
        {
            try
            {
                var thisAssembly = typeof(Plugin).Assembly;
                var requested = new AssemblyName(args.Name);

                if (args.RequestingAssembly != null && args.RequestingAssembly != thisAssembly)
                {
                    return null;
                }

                bool isOwnDependency = thisAssembly
                    .GetReferencedAssemblies()
                    .Any(a => string.Equals(a.Name, requested.Name, StringComparison.OrdinalIgnoreCase));
                if (!isOwnDependency)
                {
                    return null;
                }

                string pluginsDir = Path.GetDirectoryName(thisAssembly.Location);
                string ownFolder = Path.GetFileNameWithoutExtension(thisAssembly.Location);
                string ownSubfolder = Path.Combine(pluginsDir, ownFolder);
                string candidate = Path.Combine(ownSubfolder, requested.Name + ".dll");

                if (!File.Exists(candidate))
                {
                    return null;
                }

                if (requested.Version != null)
                {
                    var candidateName = AssemblyName.GetAssemblyName(candidate);
                    if (candidateName.Version != null && candidateName.Version < requested.Version)
                    {
                        return null;
                    }
                }

                return Assembly.LoadFrom(candidate);
            }
            catch
            {
                return null;
            }
        }
    }
}
