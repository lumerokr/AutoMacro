using System;
using System.Globalization;

namespace AutoMacro
{
    internal static class TextLimits
    {
        // Keep the existing UTF-16 storage limit without cutting a text element in half.
        internal static string Truncate(string value, int maximum)
        {
            if (value == null) throw new ArgumentNullException("value");
            if (maximum < 0) throw new ArgumentOutOfRangeException("maximum");
            if (value.Length <= maximum) return value;
            int end = 0;
            TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(value);
            while (elements.MoveNext())
            {
                int next = elements.ElementIndex + elements.GetTextElement().Length;
                if (next > maximum) break;
                end = next;
            }
            return value.Substring(0, end);
        }
    }
}
