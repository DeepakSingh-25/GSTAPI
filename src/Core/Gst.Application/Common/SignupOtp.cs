using Gst.Domain.Constants;
using Gst.Domain.Entities;

namespace Gst.Application.Common
{
    // Signup OTP banane + email text ek jagah (register / login / resend teeno use karte hain)
    public static class SignupOtp
    {
        public const int ValidMinutes = 10;
        public const string EmailSubject = "Your GST Invoice verification code";

        // Plain code sirf email ke liye memory mein; DB mein hash jaata hai. Add/save caller karta hai.
        public static (OtpCode Otp, string Code) Create(User user, DateTime now)
        {
            var code = OtpGenerator.Generate();
            var otp = new OtpCode
            {
                UserId = user.Id,
                Email = user.Email,
                Purpose = OtpPurposes.Signup,
                ExpiresOn = now.AddMinutes(ValidMinutes),
                Attempts = 0
            };
            otp.CodeHash = OtpGenerator.Hash(otp.Id, code);
            return (otp, code);
        }

        public static string EmailBody(string code) =>
            $"Your code is {code}. It is valid for {ValidMinutes} minutes. Do not share it with anyone.";
    }
}
