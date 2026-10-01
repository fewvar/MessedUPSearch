using System;
using System.Threading;
using System.Threading.Tasks;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using MimeKit.Utils;

namespace MessedUpSearchA.Services.Mail;

public record MailAccount(string Address, string SenderName, string Password, string SmtpHost, int SmtpPort);

/// <summary>Что пошло не так: от этого зависит, продолжать ли рассылку.</summary>
public enum MailFailure { None, Auth, Connection, Recipient, Other }

public class MailSendException(MailFailure failure, string message, Exception? inner = null)
    : Exception(message, inner)
{
    public MailFailure Failure { get; } = failure;
}

/// <summary>
/// Отправка через SMTP собственной почты битмейкера (MailKit). Письмо — обычный текст:
/// так оно выглядит как личное, а не как рассылка, и реже попадает в спам.
/// </summary>
public static class MailService
{
    /// <returns>Message-ID ушедшего письма — по нему потом узнаются ответы.</returns>
    public static async Task<string> SendAsync(MailAccount account, string to, string subject, string body,
        string? inReplyTo = null, CancellationToken ct = default)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(account.SenderName, account.Address));

        try
        {
            message.To.Add(MailboxAddress.Parse(to.Trim()));
        }
        catch (ParseException ex)
        {
            throw new MailSendException(MailFailure.Recipient, $"адрес «{to}» не похож на почту", ex);
        }

        message.Subject = subject;
        message.Body = new TextPart("plain") { Text = body };

        var domain = account.Address[(account.Address.LastIndexOf('@') + 1)..];
        message.MessageId = MimeUtils.GenerateMessageId(domain);

        // Фоллоу-ап — ответ в той же переписке, а не новое письмо.
        if (!string.IsNullOrWhiteSpace(inReplyTo))
        {
            message.InReplyTo = inReplyTo;
            message.References.Add(inReplyTo);
        }

        using var client = new SmtpClient { Timeout = 30_000 };
        try
        {
            await client.ConnectAsync(account.SmtpHost, account.SmtpPort, SecurityFor(account.SmtpPort), ct);
            await client.AuthenticateAsync(account.Address, account.Password, ct);
            await client.SendAsync(message, ct);
            await client.DisconnectAsync(true, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (AuthenticationException ex)
        {
            throw new MailSendException(MailFailure.Auth, ex.Message, ex);
        }
        catch (SmtpCommandException ex) when (ex.ErrorCode is SmtpErrorCode.RecipientNotAccepted)
        {
            throw new MailSendException(MailFailure.Recipient, ex.Message, ex);
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or SslHandshakeException
                                       or TimeoutException or ServiceNotConnectedException or System.IO.IOException)
        {
            throw new MailSendException(MailFailure.Connection, ex.Message, ex);
        }
        catch (Exception ex)
        {
            throw new MailSendException(MailFailure.Other, ex.Message, ex);
        }

        return message.MessageId;
    }

    public static SecureSocketOptions SecurityFor(int port) => port switch
    {
        465 or 993 => SecureSocketOptions.SslOnConnect,
        587 or 143 => SecureSocketOptions.StartTls,
        _ => SecureSocketOptions.Auto
    };
}
