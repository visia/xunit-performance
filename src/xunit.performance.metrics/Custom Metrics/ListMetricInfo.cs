using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Microsoft.Xunit.Performance
{
    public class ListMetricInfo
    {
        Metrics Name = new Metrics();
        Metrics Size = new Metrics();
        Metrics Count = new Metrics();

        Dictionary<string, SizeCount> _Items = new Dictionary<string, SizeCount>();

        public Dictionary<string, SizeCount> Items { get { return _Items; } }

        public IEnumerable<Metrics> MetricList { get { return new Metrics[] { Name, Size, Count }; } }

        public bool hasCount = false;

        public bool hasBytes = false;

        public long bytes
        {
            get
            {
                if (hasBytes == false)
                    return 0;
                long size = 0;
                foreach (var item in Items)
                {
                    size += item.Value.Size;
                }
                return size;
            }
        }
        
        public long count
        {
            get
            {
                if (hasCount == false)
                    return 0;
                long count = 0;
                foreach(var item in Items)
                {
                    count += item.Value.Count;
                }
                return count;
            }
        }

        public ListMetricInfo()
        {
            initializeMetrics();
        }

        public void addItem(string itemName, long size)
        {
            SizeCount item;
            itemName = RemoveInvalidChars(itemName);
            if (!Items.TryGetValue(itemName, out item))
            {
                item = new SizeCount();
                Items[itemName] = item;
            }

            item.Size += size;
            item.Count++;
        }

        public void clear()
        {
            Items.Clear();
        }

        void initializeMetrics()
        {
            Name.Name = "Name";
            Name.Unit = "FileName";
            Name.Type = typeof(string);
            Size.Name = "Size";
            Size.Unit = "Bytes";
            Size.Type = typeof(Int32);
            Count.Name = "Count";
            Count.Unit = "Count";
            Count.Type = typeof(Int32);
        }

        public class Metrics
        {
            public string Name { get; set; }
            public string Unit { get; set; }
            public Type Type { get; set; }
        }

        public class SizeCount
        {
            public long Size { get; set; }
            public long Count { get; set; }

            public SizeCount()
            {
                Size = 0;
                Count = 0;
            }
        }

        //Helpers
        // http://blogs.msdn.com/b/codejunkie/archive/2008/03/14/invalid-high-surrogate-character-0xxxxx.aspx
        private static string RemoveInvalidChars(string input)
        {
            if (input == null)
                return null;

            Encoding utfencoder = UTF8Encoding.GetEncoding("UTF-8", new EncoderReplacementFallback(""), new DecoderReplacementFallback(""));
            byte[] byteText = utfencoder.GetBytes(input);
            string output = utfencoder.GetString(byteText);
            string encoded = System.Xml.XmlConvert.EncodeName(output);
            string decoded = System.Xml.XmlConvert.DecodeName(encoded);
            var regex = "[\x00-\x08\x0B\x0C\x0E-\x1F]";
            string regexRemove = Regex.Replace(decoded, regex, String.Empty, RegexOptions.Compiled);
            return regexRemove;
        }
    }
}
