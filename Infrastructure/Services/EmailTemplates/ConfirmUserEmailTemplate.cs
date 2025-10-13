using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Infrastructure.Services.EmailTemplates
{
    public class ConfirmUserEmailTemplate
    {
        public static string EmailTemplate = @"
            <!DOCTYPE html>
                <html lang=""en"">
                    <head>
                        <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />
                        <meta http-equiv=""Content-Type"" content=""text/html; charset=UTF-8""/>
                        <title>Confirm Your EmailTemplate</title>
                    </head>


                    



                    <body style=""background-color: #f6f6f6; font-family: sans-serif; -webkit-font-smoothing: antialiased; font-size: 14px; line-height: 1.4; margin: 0; padding: 0; -ms-text-size-adjust: 100%; -webkit-text-size-adjust: 100%;"">

                        <span class=""preheader"" style=""color: transparent; display: none; height: 0; max-height: 0; max-width: 0; opacity: 0; overflow: hidden; mso-hide: all; visibility: hidden; width: 0;"">This is preheader text. Some clients will show this text as a preview.</span>

                        <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" class=""body"" style=""border-collapse: separate; mso-table-lspace: 0pt; mso-table-rspace: 0pt; background-color: #f6f6f6; width: 100%;"" width=""100%"" bgcolor=""#f6f6f6"">

                            <tr>

                                <td style=""font-family: sans-serif; font-size: 14px; vertical-align: top;"" valign=""top"">&nbsp;</td>

                                <td class=""container"" style=""font-family: sans-serif; font-size: 14px; vertical-align: top; display: block; max-width: 580px; padding: 10px; width: 580px; margin: 0 auto;"" width=""580"" valign=""top"">

                                    <div class=""content"" style=""box-sizing: border-box; display: block; margin: 0 auto; max-width: 580px; padding: 10px;"">

                                        

                                        <!-- START CENTERED WHITE CONTAINER -->

                                        <table role=""presentation"" class=""main"" style=""border-collapse: separate; mso-table-lspace: 0pt; mso-table-rspace: 0pt; background: #ffffff; border-radius: 3px; width: 100%;"" width=""100%"">

                                            <!-- START MAIN CONTENT AREA -->

                                            <tr>

                                                <td class=""wrapper"" style=""font-family: sans-serif; font-size: 14px; vertical-align: top; box-sizing: border-box; padding: 20px;"" valign=""top"">

                                                    <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""border-collapse: separate; mso-table-lspace: 0pt; mso-table-rspace: 0pt; width: 100%;"" width=""100%"">

                                                        <tr>

                                                            <td style=""font-family: sans-serif; font-size: 14px; vertical-align: top;"" valign=""top"">

                                                                <p style=""font-family: sans-serif; font-size: 14px; font-weight: 900; margin: 0; margin-bottom: 20px;"">Dear {{ FirstName }},</p>


                                                                <p style=""font-family: sans-serif; line-height: 1.5; font-size: 14px; font-weight: normal; margin: 0; margin-bottom: 25px; word-wrap: break-word;"">

                                                                    <h3>Welcome to CarePro!</h3>

                                                                    Please, kindly verify your account by clicking the ""Verify my Email"" below.

                                                                    <br />

                                                                    <br />

                                                                    <a href = ""{{ verificationLink }}""> Verify my Email </a>

                                                                     
                                                                    <br />
                                                                    <br />

                                                                    This helps us ensure we have the right contact information and lets you access your account securely.                                          
                                                                    <p>If you did not sign up for CarePro, please ignore this email.</p>
                                                                    <p>Thanks,<br />The CarePro Team</p>""

                                                                </p>




                                                            </td>

                                                        </tr>

                                                    </table>

                                                </td>

                                            </tr>



                                            <!-- END MAIN CONTENT AREA -->

                                        </table>

                                        <!-- END CENTERED WHITE CONTAINER -->
                                        <!-- START FOOTER -->

                                        <div class=""footer"" style=""clear: both; margin-top: 10px; text-align: center; width: 100%;"">

                                            <table role=""presentation"" border=""0"" cellpadding=""0"" cellspacing=""0"" style=""border-collapse: separate; mso-table-lspace: 0pt; padding: 20px; border-radius: 5px; color: #ffffff;  background: #000066; mso-table-rspace: 0pt; width: 100%;"" width=""100%;"">


                                                <tr>

                                                    <td class=""content-block powered-by"" style=""font-family: sans-serif; vertical-align: top; padding-bottom: 10px; padding-top: 10px; font-size: 12px; text-align: center;"" valign=""top"" align=""center"">

                                                        <strong>Powered by <a href=""https://www.codesquare.com/"" style=""color: #F5F5F5; font-size: 12px; text-align: center; text-decoration: none;"">Havis 360</a></strong>

                                                    </td>

                                                </tr>

                                            </table>

                                        </div>

                                        <!-- END FOOTER -->



                                    </div>

                                </td>

                                <td style=""font-family: sans-serif; font-size: 14px; vertical-align: top;"" valign=""top"">&nbsp;</td>

                            </tr>

                        </table>

                    </body>



                </html>
                        ";
    }
}
