using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Zipper;

namespace Microsoft.Xunit.Performance
{
    class ZipperWrapper
    {
        private Zipper.Zipper _zipper;

        public ZipperWrapper(string zipPath, string[] filesToZip)
        {
            _zipper = new Zipper.Zipper(zipPath, filesToZip);
        }

        public void QueueAddFileOrDir(string fileOrDir)
        {
            _zipper.QueueAddFileOrDir(fileOrDir);
        }

        public void CloseZipFile()
        {
            _zipper.CloseZipFile();
        }
    }
}
