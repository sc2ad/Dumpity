using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static DLLConverter.DLLParser.DLLData;

namespace DLLConverter
{
    class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            var ofd = new OpenFileDialog();
            ofd.Filter = "DLL to convert|*.dll";
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                // Found DLL to convert
                AssemblyDefinition a = AssemblyDefinition.ReadAssembly(ofd.FileName);
                foreach (var t in a.MainModule.Types)
                {
                    foreach (var m in t.Methods)
                    {
                        if (t.Name == "BeatSaberUI")
                        {
                            if (m.Name == "CreateText")
                            {
                                var methodData = MethodData.Parse(m);
                                Console.WriteLine(methodData.Header);
                            }
                        }
                        
                    }
                }
            }
            Console.WriteLine("Complete!");
        }
    }
}
