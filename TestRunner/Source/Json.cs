using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WyuTestRunner
{
    static class Json
    {
        public static string Quote(string s) {
            if (s is null) return "null";
            var sb = new StringBuilder("\"");
            foreach (var c in s)
                switch (c) {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int) c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            return sb.Append('"').ToString();
        }

        public static string Array(IEnumerable<string> rawItems) => "[" + string.Join(",", rawItems) + "]";
        public static string StringArray(IEnumerable<string> items) {
            var l = new List<string>();
            foreach (var i in items) l.Add(Quote(i));
            return Array(l);
        }
        public static string Obj(params (string key, string raw)[] fields) {
            var l = new List<string>();
            foreach (var (k, v) in fields) l.Add(Quote(k) + ":" + v);
            return "{" + string.Join(",", l) + "}";
        }
        public static string Num(long n)   => n.ToString(CultureInfo.InvariantCulture);
        public static string Dbl(double d) => d.ToString("R", CultureInfo.InvariantCulture);
        public static string Bool(bool b)  => b ? "true" : "false";
    }
}
