using Mail.DomainService.Entities;
using MimeKit;

namespace Mail.DomainService.Mails
{
    /// <summary>
    /// Builds the MIME message. One composer for every MailKit-based sender.
    /// </summary>
    /// <remarks>
    /// Extracted from <see cref="MailKitSmtpClient"/> unchanged so the legacy providers keep
    /// producing the same bytes they always have, and so the Office 365 sender cannot drift into a
    /// second, subtly different message shape. Provider-specific headers are deliberately not
    /// composed here — they are the one part that differs, and each sender adds its own.
    /// </remarks>
    public static class MailMessageComposer
    {
        public static MimeMessage Compose(MailToBeSent mailToBeSent, MailBody mailBody)
        {
            var message = new MimeMessage
            {
                Subject = mailBody.Subject,
            };

            var bodyBuilder = new BodyBuilder
            {
                HtmlBody = mailBody.Body
            };

            foreach (var attachment in mailBody.Attachments)
            {
                bodyBuilder.Attachments.Add(attachment.FileName, attachment.Content, ParseContentType(attachment.ContentType));
            }

            // ToMessageBody() snapshots the builder, so this has to stay below the attachment loop.
            message.Body = bodyBuilder.ToMessageBody();
            message.From.Add(new MailboxAddress(mailToBeSent.MailServerConfiguration.SenderName, mailToBeSent.MailServerConfiguration.SenderAddress));

            foreach (var recipient in mailToBeSent.To)
            {
                message.To.Add(MailboxAddress.Parse(recipient));
            }

            if (mailToBeSent.Cc != null)
            {
                foreach (var cc in mailToBeSent.Cc)
                {
                    message.Cc.Add(MailboxAddress.Parse(cc));
                }
            }

            if (mailToBeSent.Bcc != null)
            {
                foreach (var bcc in mailToBeSent.Bcc)
                {
                    message.Bcc.Add(MailboxAddress.Parse(bcc));
                }
            }

            if (mailToBeSent.ReplyTo != null)
            {
                foreach (var replyTo in mailToBeSent.ReplyTo)
                {
                    message.ReplyTo.Add(MailboxAddress.Parse(replyTo));
                }
            }

            return message;
        }

        public static ContentType ParseContentType(string? contentType)
        {
            if (!string.IsNullOrWhiteSpace(contentType) && ContentType.TryParse(contentType, out var parsed))
            {
                return parsed;
            }

            return new ContentType("application", "octet-stream");
        }
    }
}
