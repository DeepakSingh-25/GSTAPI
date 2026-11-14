using System.ComponentModel;

namespace Gst.Domain.Enums
{
    public enum OperationType
    {
        Read = 1,
        Insert = 2,
        Update = 4,
        Delete = 8
    }

    public static class EnumExtensions
    {
        public static string GetDescription(this Enum value)
        {
            var field = value.GetType().GetField(value.ToString());
            return field != null && Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) is DescriptionAttribute attr
                ? attr.Description
                : value.ToString();
        }
    }
}
