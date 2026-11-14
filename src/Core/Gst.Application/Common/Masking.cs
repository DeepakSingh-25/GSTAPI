namespace Gst.Application.Common
{
    public static class Masking
    {
        // deepak@demotraders.in -> d****k@demotraders.in
        public static string MaskEmail(string email)
        {
            var at = email.LastIndexOf('@');
            if (at <= 0) return "***";

            var local = email[..at];
            var domain = email[(at + 1)..];

            if (local.Length == 1) return local[0] + "**@" + domain;
            return local[0] + new string('*', Math.Max(2, local.Length - 2)) + local[^1] + "@" + domain;
        }
    }
}
