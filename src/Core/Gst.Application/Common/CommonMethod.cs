namespace Gst.Application.Common
{
    public static class CommonMethod
    {
        public static string GetErrorMessage(Exception ex) => ex.InnerException?.Message ?? ex.Message;
    }
}
