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
    // Iconos: placeholder transparente 1x1 — decisión explícita, igual que hizo
    // DataverseMasterDataMigrator en sus primeras versiones. Reemplazar por íconos reales más
    // adelante y volver a correr tools/verify_attr_blobs.py del repo hermano contra este DLL.
    [Export(typeof(IXrmToolBoxPlugin)),
        ExportMetadata("Name", "Umayor Test Data Seeder"),
        ExportMetadata("Description", "Extrae el grafo de registros de un RUT/pasaporte desde Producción, lo anonimiza y lo migra a un entorno bajo."),
        ExportMetadata("PluginType", "DataMigration"),
        ExportMetadata("SmallImageBase64", "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="),
        ExportMetadata("BigImageBase64", "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="),
        ExportMetadata("BackgroundColor", "Lavender"),
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
