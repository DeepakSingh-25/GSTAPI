namespace Gst.Domain.Constants
{
    // State name -> GST state code (37 entries, 25 Daman and Diu legacy rakha hai)
    public static class IndianStates
    {
        private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Jammu and Kashmir"] = "01",
            ["Himachal Pradesh"] = "02",
            ["Punjab"] = "03",
            ["Chandigarh"] = "04",
            ["Uttarakhand"] = "05",
            ["Haryana"] = "06",
            ["Delhi"] = "07",
            ["Rajasthan"] = "08",
            ["Uttar Pradesh"] = "09",
            ["Bihar"] = "10",
            ["Sikkim"] = "11",
            ["Arunachal Pradesh"] = "12",
            ["Nagaland"] = "13",
            ["Manipur"] = "14",
            ["Mizoram"] = "15",
            ["Tripura"] = "16",
            ["Meghalaya"] = "17",
            ["Assam"] = "18",
            ["West Bengal"] = "19",
            ["Jharkhand"] = "20",
            ["Odisha"] = "21",
            ["Chhattisgarh"] = "22",
            ["Madhya Pradesh"] = "23",
            ["Gujarat"] = "24",
            ["Daman and Diu"] = "25",
            ["Dadra and Nagar Haveli and Daman and Diu"] = "26",
            ["Maharashtra"] = "27",
            ["Karnataka"] = "29",
            ["Goa"] = "30",
            ["Lakshadweep"] = "31",
            ["Kerala"] = "32",
            ["Tamil Nadu"] = "33",
            ["Puducherry"] = "34",
            ["Andaman and Nicobar Islands"] = "35",
            ["Telangana"] = "36",
            ["Andhra Pradesh"] = "37",
            ["Ladakh"] = "38"
        };

        public static bool Contains(string? name) => name is not null && Map.ContainsKey(name);

        public static bool TryGetCode(string? name, out string code)
        {
            code = string.Empty;
            return name is not null && Map.TryGetValue(name, out code!);
        }

        // Canonical (sahi spelling/case wala) naam
        public static string? GetCanonicalName(string? name)
        {
            if (name is null) return null;
            foreach (var key in Map.Keys)
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                    return key;
            return null;
        }
    }
}
