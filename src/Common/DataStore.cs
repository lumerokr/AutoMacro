using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;


namespace AutoMacro
{
    internal class DataField
    {
        internal readonly string Name;
        internal readonly string Value;
        internal Action RemoveAction;
        internal DataField(string name, object value) { Name = name; Value = Convert.ToString(value, CultureInfo.InvariantCulture); }
        internal void Remove() { if (RemoveAction != null) RemoveAction(); }
        public static explicit operator string(DataField value) { return value == null ? null : value.Value; }
        public static explicit operator int(DataField value) { return Int32.Parse(value.Value, CultureInfo.InvariantCulture); }
        public static explicit operator long(DataField value) { return Int64.Parse(value.Value, CultureInfo.InvariantCulture); }
        public static explicit operator bool(DataField value) { return Boolean.Parse(value.Value); }
        public static explicit operator int?(DataField value) { return value == null ? (int?)null : (int)value; }
        public static explicit operator bool?(DataField value) { return value == null ? (bool?)null : (bool)value; }
    }
    internal sealed class DataNode : DataField
    {
        readonly List<DataField> fields = new List<DataField>();
        readonly List<DataNode> children = new List<DataNode>();
        internal DataNode(string name, params object[] values) : base(name, values.Length == 1 && !(values[0] is DataField) ? values[0] : "")
        { foreach (object value in values) if (value is DataField) Add(value); }
        internal void Add(object value)
        {
            DataNode child = value as DataNode;
            if (child != null) { children.Add(child); child.RemoveAction = delegate { children.Remove(child); }; }
            else { DataField field = (DataField)value; fields.Add(field); field.RemoveAction = delegate { fields.Remove(field); }; }
        }
        internal IEnumerable<DataField> Attributes() { return fields; }
        internal IEnumerable<DataNode> Elements() { return children; }
        internal IEnumerable<DataNode> Elements(string name) { return children.FindAll(delegate(DataNode item) { return item.Name == name; }); }
        internal DataField Attribute(string name) { return fields.Find(delegate(DataField item) { return item.Name == name; }); }
        internal DataNode Element(string name) { return children.Find(delegate(DataNode item) { return item.Name == name; }); }
    }
    internal static class DataStore
    {
        internal static bool Exists(string path) { return File.Exists(path); }
        static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = 150 * 1024 * 1024, RecursionLimit = 100 }; }
        internal static DataNode Load(string path)
        {
            if (new FileInfo(path).Length > 150 * 1024 * 1024) throw new FormatException("Data file is too large");
            Dictionary<string, object> document = Serializer().DeserializeObject(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
            if (document == null || !document.ContainsKey("type")) throw new FormatException("Invalid JSON document");
            string type = document["type"] as string;
            if (type != "AutoMacro" && type != "MacroLibrary") throw new FormatException("Unknown data type");
            return Decode(type, document);
        }
        static object Scalar(string value)
        {
            bool flag; long integer;
            if (Boolean.TryParse(value, out flag)) return flag;
            if (Int64.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out integer)) return integer;
            return value;
        }
        static Dictionary<string, object> Encode(DataNode element)
        {
            Dictionary<string, object> value = new Dictionary<string, object>();
            if (element.Name == "AutoMacro" || element.Name == "MacroLibrary") value.Add("type", element.Name);
            foreach (DataField attribute in element.Attributes())
                value.Add(attribute.Name, attribute.Name == "name" || attribute.Name == "id" || attribute.Name == "created" || attribute.Name == "language" ? (object)attribute.Value : Scalar(attribute.Value));
            if (element.Name == "MacroLibrary" || element.Name == "Macro")
            {
                List<object> list = new List<object>();
                foreach (DataNode child in element.Elements()) list.Add(Encode(child));
                value.Add(element.Name == "MacroLibrary" ? "macros" : "actions", list);
            }
            else foreach (DataNode child in element.Elements())
            {
                string key = child.Name;
                value.Add(Char.ToLowerInvariant(key[0]) + key.Substring(1), Scalar(child.Value));
            }
            return value;
        }
        static DataNode Decode(string type, Dictionary<string, object> value)
        {
            DataNode element = new DataNode(type);
            foreach (KeyValuePair<string, object> pair in value)
            {
                if (pair.Key == "type") continue;
                if (pair.Key == "macros" || pair.Key == "actions")
                {
                    if ((type != "MacroLibrary" || pair.Key != "macros") && (type != "Macro" || pair.Key != "actions")) throw new FormatException("Invalid data array");
                    IList children = pair.Value as IList;
                    if (children == null) throw new FormatException("Invalid data array");
                    foreach (object child in children)
                    {
                        Dictionary<string, object> entry = child as Dictionary<string, object>;
                        if (entry == null) throw new FormatException("Invalid data entry");
                        element.Add(Decode(pair.Key == "macros" ? "Macro" : "Action", entry));
                    }
                }
                else
                {
                    if (pair.Value == null || pair.Value is IDictionary || pair.Value is IList || pair.Key.Length == 0) throw new FormatException("Invalid scalar");
                    string scalar = pair.Value is bool ? ((bool)pair.Value ? "true" : "false") : System.Convert.ToString(pair.Value, CultureInfo.InvariantCulture);
                    if (type == "AutoMacro" && pair.Key != "version") element.Add(new DataNode(Char.ToUpperInvariant(pair.Key[0]) + pair.Key.Substring(1), scalar));
                    else element.Add(new DataField(pair.Key, scalar));
                }
            }
            return element;
        }
        internal static void Save(string path, DataNode value, bool backup)
        {
            string temp = path + ".tmp";
            try
            {
                File.WriteAllText(temp, Pretty(Serializer().Serialize(Encode(value))), new UTF8Encoding(false));
                if (File.Exists(path)) File.Replace(temp, path, backup ? path + ".bak" : null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        static string Pretty(string json)
        {
            StringBuilder result = new StringBuilder(); int depth = 0; bool quoted = false, escaped = false;
            foreach (char c in json)
            {
                if (quoted)
                {
                    result.Append(c);
                    if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false;
                    continue;
                }
                if (c == '"') { quoted = true; result.Append(c); }
                else if (c == '{' || c == '[') { result.Append(c).AppendLine(); depth++; result.Append(' ', depth * 2); }
                else if (c == '}' || c == ']') { result.AppendLine(); depth--; result.Append(' ', depth * 2).Append(c); }
                else if (c == ',') result.Append(c).AppendLine().Append(' ', depth * 2);
                else if (c == ':') result.Append(": ");
                else if (!Char.IsWhiteSpace(c)) result.Append(c);
            }
            return result.AppendLine().ToString();
        }
    }
}
