using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Text.Unicode;
using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;
using Volo.Abp;
using Volo.Abp.DependencyInjection;

namespace Workmate.Poc.Application.Notifications;

/// <summary>Everything a template may show (research R9). Null values render empty.</summary>
public record EmailModel(
    string? RequestTitle, string? RequestNumber, string? RequestStatus,
    string? InitiatorName, string? ApproverName, string? StepTitle, string? Comment,
    string? Link, string CompanyName, IReadOnlyDictionary<string, string?>? Fields);

public record RenderedEmail(string Subject, string Text, string Html);

/// <summary>
/// Sandboxed Scriban for admin-edited templates (FR-004, research R4/R5). Validation walks the syntax
/// tree and allows only text, allow-listed placeholders and if/else/end; rendering uses a fresh
/// TemplateContext with no template loader, so even a template that slipped past validation can't include
/// files or call into anything but the values given to it.
/// </summary>
public class EmailTemplateRenderer : ITransientDependency
{
    public const int MaxSubjectLength = 200;

    private static readonly HashSet<string> AllowedPaths = new()
    {
        "request.title", "request.number", "request.status", "initiator.name", "approver.name",
        "step.title", "comment", "link", "company.name"
    };

    private static readonly HtmlEncoder Html = HtmlEncoder.Create(UnicodeRanges.All);
    private static readonly Regex Url = new(@"https?://[^\s<]+", RegexOptions.Compiled);

    /// <summary><paramref name="allowedFieldKeys"/> null means <c>fields.*</c> isn't allowed at all (company defaults).</summary>
    public void Validate(string subject, string body, ISet<string>? allowedFieldKeys)
    {
        if (string.IsNullOrWhiteSpace(subject)) throw new UserFriendlyException("Subject is required.");
        if (string.IsNullOrWhiteSpace(body)) throw new UserFriendlyException("Body is required.");
        ValidateOne("Subject", subject, allowedFieldKeys);
        ValidateOne("Body", body, allowedFieldKeys);
    }

    public RenderedEmail Render(string subject, string body, EmailModel model)
    {
        var renderedSubject = RenderText(subject, model).Replace("\r", " ").Replace("\n", " ").Trim();
        if (renderedSubject.Length > MaxSubjectLength) renderedSubject = renderedSubject[..MaxSubjectLength];

        var text = RenderText(body, model).Replace("\r\n", "\n");
        return new RenderedEmail(renderedSubject, text, ToHtml(text, model));
    }

    private static void ValidateOne(string part, string source, ISet<string>? allowedFieldKeys)
    {
        var template = Template.Parse(source);
        if (template.HasErrors)
            throw new UserFriendlyException($"{part}: {template.Messages.First().Message}");
        CheckNode(template.Page, allowedFieldKeys);
    }

    private static void CheckNode(ScriptNode? node, ISet<string>? allowedFieldKeys)
    {
        switch (node)
        {
            case null:
            case ScriptRawStatement:
            case ScriptEscapeStatement: // the {{ and }} delimiters themselves
            case ScriptNopStatement:
            case ScriptEndStatement:
                return;
            case ScriptPage page:
                CheckNode(page.Body, allowedFieldKeys);
                return;
            case ScriptBlockStatement block:
                foreach (var statement in block.Statements) CheckNode(statement, allowedFieldKeys);
                return;
            case ScriptExpressionStatement expression:
                CheckPlaceholder(expression.Expression, allowedFieldKeys);
                return;
            case ScriptIfStatement ifStatement:
                CheckCondition(ifStatement.Condition, allowedFieldKeys);
                CheckNode(ifStatement.Then, allowedFieldKeys);
                CheckNode(ifStatement.Else, allowedFieldKeys);
                return;
            case ScriptElseStatement elseStatement:
                CheckNode(elseStatement.Body, allowedFieldKeys);
                return;
            default:
                throw Refuse(node);
        }
    }

    /// <summary>A condition is a placeholder, or "not" a placeholder (<c>{{ if !comment }}</c>).</summary>
    private static void CheckCondition(ScriptExpression condition, ISet<string>? allowedFieldKeys)
    {
        if (condition is ScriptUnaryExpression { Operator: ScriptUnaryOperator.Not } not)
            CheckPlaceholder(not.Right, allowedFieldKeys);
        else
            CheckPlaceholder(condition, allowedFieldKeys);
    }

    private static void CheckPlaceholder(ScriptExpression expression, ISet<string>? allowedFieldKeys)
    {
        var path = PathOf(expression) ?? throw Refuse(expression);
        if (AllowedPaths.Contains(path)) return;
        if (path.StartsWith("fields.") && allowedFieldKeys != null && allowedFieldKeys.Contains(path["fields.".Length..])) return;
        throw new UserFriendlyException($"Unknown placeholder '{{{{ {path} }}}}'.");
    }

    /// <summary>"a.b.c" for plain variables and member access; null for anything else (calls, pipes, indexers…).</summary>
    private static string? PathOf(ScriptExpression expression) => expression switch
    {
        ScriptVariableGlobal v => v.Name,
        ScriptMemberExpression { Target: var target, Member: ScriptVariable member } =>
            PathOf(target) is { } parent ? parent + "." + member.Name : null,
        _ => null
    };

    private static UserFriendlyException Refuse(ScriptNode node)
    {
        var call = node switch
        {
            ScriptExpressionStatement { Expression: ScriptFunctionCall f } => f,
            ScriptFunctionCall f => f,
            _ => null
        };
        if (call?.Target is ScriptVariable { Name: "include" or "import" } fn)
            return new UserFriendlyException($"'{fn.Name}' is not allowed in email templates.");
        if (node is ScriptImportStatement)
            return new UserFriendlyException("'import' is not allowed in email templates.");

        var text = node.ToString()?.Trim() ?? node.GetType().Name;
        if (text.Length > 60) text = text[..60] + "…";
        return new UserFriendlyException($"'{{{{ {text} }}}}' is not allowed in email templates. Use placeholders and if/else/end only.");
    }

    private static string RenderText(string source, EmailModel model)
    {
        var template = Template.Parse(source);
        if (template.HasErrors) throw new UserFriendlyException(template.Messages.First().Message);

        var context = new TemplateContext { TemplateLoader = null, LoopLimit = 100, RecursiveLimit = 20 };
        context.PushGlobal(ToScriptObject(model));
        return template.Render(context);
    }

    private static ScriptObject ToScriptObject(EmailModel m)
    {
        // Empty strings become null so that {{ if comment }} is false for "no comment".
        static string? V(string? s) => string.IsNullOrEmpty(s) ? null : s;

        var fields = new ScriptObject();
        foreach (var (key, value) in m.Fields ?? new Dictionary<string, string?>())
            fields.SetValue(key, V(value), readOnly: true);

        var root = new ScriptObject();
        root.SetValue("request", Obj(("title", V(m.RequestTitle)), ("number", V(m.RequestNumber)), ("status", V(m.RequestStatus))), true);
        root.SetValue("initiator", Obj(("name", V(m.InitiatorName))), true);
        root.SetValue("approver", Obj(("name", V(m.ApproverName))), true);
        root.SetValue("step", Obj(("title", V(m.StepTitle))), true);
        root.SetValue("company", Obj(("name", V(m.CompanyName))), true);
        root.SetValue("comment", V(m.Comment), true);
        root.SetValue("link", V(m.Link), true);
        root.SetValue("fields", fields, true);
        return root;
    }

    private static ScriptObject Obj(params (string Key, string? Value)[] values)
    {
        var o = new ScriptObject();
        foreach (var (key, value) in values) o.SetValue(key, value, readOnly: true);
        return o;
    }

    /// <summary>The HTML part is the encoded text in a fixed layout, so field values are shown, never interpreted (US2 AC2).</summary>
    private static string ToHtml(string text, EmailModel model)
    {
        var body = string.Join("<br>\n", text.Split('\n').Select(line => Url.Replace(Html.Encode(line), u => $"<a href=\"{u.Value}\">{u.Value}</a>")));
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><body style=\"margin:0;background:#f4f5f7;font-family:Segoe UI,Arial,sans-serif;color:#1f2933\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\"><tr><td align=\"center\" style=\"padding:24px 12px\">");
        sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" style=\"max-width:600px;background:#ffffff;border-radius:8px;overflow:hidden\">");
        sb.Append($"<tr><td style=\"background:#2457a6;color:#ffffff;padding:16px 24px;font-size:18px;font-weight:600\">{Html.Encode(model.CompanyName)}</td></tr>");
        sb.Append($"<tr><td style=\"padding:24px;font-size:15px;line-height:1.5\">{body}");
        if (!string.IsNullOrEmpty(model.Link))
        {
            var href = Html.Encode(model.Link);
            sb.Append($"<p style=\"margin:24px 0 0\"><a href=\"{href}\" style=\"display:inline-block;background:#2457a6;color:#ffffff;padding:10px 18px;border-radius:6px;text-decoration:none\">Open in Workmate</a></p>");
        }
        sb.Append("</td></tr><tr><td style=\"padding:12px 24px;font-size:12px;color:#6b7785;border-top:1px solid #e4e7eb\">Sent by Workmate.</td></tr>");
        sb.Append("</table></td></tr></table></body></html>");
        return sb.ToString();
    }
}
