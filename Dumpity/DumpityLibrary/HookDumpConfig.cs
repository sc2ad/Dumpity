using System;
using System.Collections.Generic;
using System.Text;

namespace DumpityLibrary
{
    public class HookDumpConfig
    {
        public HookDumpConfig(string file, bool onlyHeader)
        {
            FileName = file;
            FileHeaderName = file.Replace(".c", ".h");
            DumpHookCreation = true;
            DumpHookInstallation = true;
            DumpStructs = true;
            OnlyMakeHeader = onlyHeader;
        }
        internal string FileName { get; set; }
        internal string FileHeaderName { get; set; }
        private bool _dumpHookCreation;
        internal bool DumpHookCreation
        {
            get => _dumpHookCreation;
            set
            {
                _dumpHookCreation = value;
                if (_dumpHookCreation)
                {
                    _onlyMakeHeader = false;
                }
                else
                {
                    _dumpHookInstallation = false;
                }
            }
        }
        private bool _dumpHookInstallation;
        internal bool DumpHookInstallation
        {
            get => _dumpHookInstallation;
            set
            {
                _dumpHookInstallation = value;
                if (_dumpHookInstallation)
                {
                    _onlyMakeHeader = false;
                    _dumpHookCreation = true;
                }
            }
        }
        internal bool DumpStructs { get; set; }
        private bool _onlyMakeHeader;
        internal bool OnlyMakeHeader
        {
            get => _onlyMakeHeader;
            set
            {
                _onlyMakeHeader = value;
                if (_onlyMakeHeader)
                {
                    _dumpHookCreation = false;
                    _dumpHookInstallation = false;
                }
            }
        }

    }
}
