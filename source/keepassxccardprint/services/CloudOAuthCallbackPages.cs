using KeePassXC.CloudTransport;
using KeePassXCCardPrint.ViewModels;

namespace KeePassXCCardPrint.Services;

/// <summary>Host-owned, self-contained OAuth result pages. No user/account data,
/// tokens, URLs, scripts or remote resources are ever interpolated into the HTML.</summary>
internal static class CloudOAuthCallbackPages
{
    internal static GoogleOAuthCallbackPages Create(string language, bool darkTheme)
        => new(Generate(language, darkTheme, succeeded: true),
               Generate(language, darkTheme, succeeded: false));

    private static string Generate(string language, bool darkTheme, bool succeeded)
    {
        var lang = Localizer.ResolveLanguage(language);
        var headline = succeeded ? lang switch
        {
            "uk" => "Авторизацію завершено",
            "en" => "Authorization complete",
            _ => "Авторизация завершена"
        } : lang switch
        {
            "uk" => "Авторизацію не завершено",
            "en" => "Authorization not completed",
            _ => "Авторизация не завершена"
        };
        var description = succeeded ? lang switch
        {
            "uk" => "Обліковий запис Google Drive успішно підключено до KeePassXC Card Print.",
            "en" => "Your Google Drive account is connected to KeePassXC Card Print.",
            _ => "Аккаунт Google Drive успешно подключён к KeePassXC Card Print."
        } : lang switch
        {
            "uk" => "Не вдалося підтвердити підключення. Поверніться до програми й повторіть спробу.",
            "en" => "The connection could not be confirmed. Return to the application and try again.",
            _ => "Не удалось подтвердить подключение. Вернитесь в программу и повторите попытку."
        };
        var instruction = succeeded ? lang switch
        {
            "uk" => "Можна закрити цю вкладку й повернутися до програми.",
            "en" => "You can close this tab and return to the application.",
            _ => "Можно закрыть эту вкладку и вернуться в программу."
        } : lang switch
        {
            "uk" => "Закрийте цю вкладку та перевірте налаштування підключення.",
            "en" => "Close this tab and check your connection settings.",
            _ => "Закройте эту вкладку и проверьте настройки подключения."
        };
        var state = succeeded ? lang switch
        {
            "uk" => "ПІДКЛЮЧЕНО", "en" => "CONNECTED", _ => "ПОДКЛЮЧЕНО"
        } : lang switch
        {
            "uk" => "НЕ ПІДКЛЮЧЕНО", "en" => "NOT CONNECTED", _ => "НЕ ПОДКЛЮЧЕНО"
        };
        var footer = lang switch
        {
            "uk" => "Безпечне локальне завершення входу",
            "en" => "Secure local sign-in completion",
            _ => "Защищённое локальное завершение входа"
        };
        var background = darkTheme ? "#10151e" : "#eef3fa";
        var surface = darkTheme ? "#1a2433" : "#ffffff";
        var surface2 = darkTheme ? "#151e2b" : "#f6f9fd";
        var text = darkTheme ? "#eff5ff" : "#162338";
        var subtle = darkTheme ? "#a4b3c9" : "#52637e";
        var outline = darkTheme ? "#334254" : "#d9e2ed";
        var accent = succeeded ? "#48ccad" : "#ffa870";
        // Microsoft Fluent UI System Icons (MIT), 24 Regular: checkmark, error circle,
        // lock closed and contact card. Exact source paths; only the fill is themed.
        // https://github.com/microsoft/fluentui-system-icons
        var checkPath = succeeded
            ? "M4.53033 12.9697C4.23744 12.6768 3.76256 12.6768 3.46967 12.9697C3.17678 13.2626 3.17678 13.7374 3.46967 14.0303L7.96967 18.5303C8.26256 18.8232 8.73744 18.8232 9.03033 18.5303L20.0303 7.53033C20.3232 7.23744 20.3232 6.76256 20.0303 6.46967C19.7374 6.17678 19.2626 6.17678 18.9697 6.46967L8.5 16.9393L4.53033 12.9697Z"
            : "M12 2C17.5228 2 22 6.47715 22 12C22 17.5228 17.5228 22 12 22C6.47715 22 2 17.5228 2 12C2 6.47715 6.47715 2 12 2ZM12 3.5C7.30558 3.5 3.5 7.30558 3.5 12C3.5 16.6944 7.30558 20.5 12 20.5C16.6944 20.5 20.5 16.6944 20.5 12C20.5 7.30558 16.6944 3.5 12 3.5ZM11.999 14.502C12.5504 14.5021 12.9971 14.9496 12.9971 15.501C12.997 16.0524 12.5504 16.4998 11.999 16.5C11.4475 16.5 11 16.0525 11 15.501C11 14.9494 11.4475 14.502 11.999 14.502ZM11.9941 7C12.3738 6.9997 12.6883 7.2815 12.7383 7.64746L12.7451 7.74902L12.749 12.251C12.7494 12.6652 12.4132 13.0016 11.999 13.002C11.6194 13.0021 11.3058 12.7195 11.2559 12.3535L11.249 12.252L11.2451 7.75098C11.2448 7.33687 11.5801 7.00051 11.9941 7Z";
        // Double-dollar interpolation allows literal CSS braces without escaping.
        return $$"""
<!doctype html>
<html lang="{{lang}}">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1">
  <meta name="referrer" content="no-referrer">
  <meta name="color-scheme" content="{{(darkTheme ? "dark" : "light")}}">
  <title>KeePassXC Card Print · Google Drive</title>
  <style>
    :root { color-scheme: {{(darkTheme ? "dark" : "light")}}; }
    * { box-sizing: border-box; }
    body { margin:0; min-height:100vh; display:grid; place-items:center; padding:24px;
      font-family:"Segoe UI",Inter,system-ui,-apple-system,sans-serif; color:{{text}};
      background:radial-gradient(ellipse at 18% 14%,rgba(68,148,196,.17),transparent 43%),
      radial-gradient(ellipse at 82% 84%,rgba(72,204,173,.09),transparent 45%),{{background}}; }
    main { width:min(100%,550px); padding:35px 35px 25px; background:{{surface}};
      border:1px solid {{outline}}; border-radius:18px;
      box-shadow:0 25px 80px rgba(0,0,0,.22); }
    .brand { display:flex; align-items:center; gap:15px; margin-bottom:31px; }
    .appmark { display:grid; place-items:center; width:51px; height:51px; border-radius:13px;
      background:{{surface2}}; border:1px solid {{outline}}; }
    .brandname { font-size:18px; font-weight:670; letter-spacing:-.35px; }
    .brandinfo { color:{{subtle}}; font-size:12px; margin-top:4px; }
    .rule { height:1px; background:{{outline}}; margin:0 -35px 29px; }
    .result { display:flex; align-items:center; gap:12px; margin-bottom:13px; }
    .resulticon { width:43px; height:43px; flex-shrink:0; display:grid; place-items:center;
      border-radius:50%; color:{{text}}; border:1px solid {{outline}};
      background:{{surface2}}; }
    .status { font-size:11px; font-weight:750; letter-spacing:1.5px; color:{{accent}}; }
    h1 { font-size:24px; letter-spacing:-.65px; line-height:1.25; margin:0 0 12px; }
    p { font-size:14px; color:{{subtle}}; line-height:1.65; margin:0; }
    .hint { margin-top:25px; padding:14px 16px; border:1px solid {{outline}};
      background:{{surface2}}; border-radius:10px; font-size:13px; color:{{text}}; }
    footer { display:flex; align-items:center; gap:8px; margin-top:23px;
      color:{{subtle}}; font-size:11px; }
    footer svg { color:{{subtle}}; flex-shrink:0; }
    @media(max-width:540px) { main { padding:26px 22px 22px; } .rule { margin-left:-22px; margin-right:-22px; } h1 { font-size:21px; } }
  </style>
</head>
<body>
<main role="main">
  <div class="brand">
    <div class="appmark" aria-hidden="true">
      <svg width="32" height="32" viewBox="0 0 24 24" fill="currentColor"><path d="M19.75 4C20.9926 4 22 5.00736 22 6.25V17.7546C22 18.9972 20.9926 20.0046 19.75 20.0046H4.25C3.00736 20.0046 2 18.9972 2 17.7546V6.25C2 5.00736 3.00736 4 4.25 4H19.75ZM19.75 5.5H4.25C3.83579 5.5 3.5 5.83579 3.5 6.25V17.7546C3.5 18.1688 3.83579 18.5046 4.25 18.5046H19.75C20.1642 18.5046 20.5 18.1688 20.5 17.7546V6.25C20.5 5.83579 20.1642 5.5 19.75 5.5ZM9.75 12.5C10.1642 12.5 10.5 12.8358 10.5 13.25V13.7427L10.4921 13.8513C10.3293 14.9642 9.39767 15.5009 7.99995 15.5009C6.60213 15.5009 5.67048 14.9637 5.50787 13.8501L5.5 13.7418V13.25C5.5 12.8358 5.83579 12.5 6.25 12.5H9.75ZM13.2523 12.9961H17.75C18.1642 12.9961 18.5 13.3319 18.5 13.7461C18.5 14.1258 18.2178 14.4396 17.8518 14.4893L17.75 14.4961H13.2523C12.8381 14.4961 12.5023 14.1604 12.5023 13.7461C12.5023 13.3664 12.7844 13.0526 13.1505 13.003L13.2523 12.9961ZM8 8.50218C8.82841 8.50218 9.49997 9.17374 9.49997 10.0022C9.49997 10.8306 8.82841 11.5021 8 11.5021C7.17159 11.5021 6.50003 10.8306 6.50003 10.0022C6.50003 9.17374 7.17159 8.50218 8 8.50218ZM13.2523 9.5H17.75C18.1642 9.5 18.5 9.83579 18.5 10.25C18.5 10.6297 18.2178 10.9435 17.8518 10.9932L17.75 11H13.2523C12.8381 11 12.5023 10.6642 12.5023 10.25C12.5023 9.8703 12.7844 9.55651 13.1505 9.50685L13.2523 9.5H17.75H13.2523Z"/></svg>
    </div>
    <div><div class="brandname">KeePassXC Card Print</div><div class="brandinfo">Google Drive · OAuth 2.0</div></div>
  </div>
  <div class="rule"></div>
  <div class="result">
    <div class="resulticon" aria-hidden="true">
      <svg width="25" height="25" viewBox="0 0 24 24" fill="currentColor"><path d="{{checkPath}}"/></svg>
    </div>
    <span class="status">{{state}}</span>
  </div>
  <h1>{{headline}}</h1>
  <p>{{description}}</p>
  <div class="hint">{{instruction}}</div>
  <footer>
    <svg width="15" height="15" viewBox="0 0 24 24" fill="currentColor"><path d="M12 1C14.7614 1 17 3.23858 17 6V8.00977C18.6781 8.13743 20 9.5392 20 11.25V18.75C20 20.5449 18.5449 22 16.75 22H7.25C5.45507 22 4 20.5449 4 18.75V11.25C4 9.5392 5.3219 8.13743 7 8.00977V6C7 3.23858 9.23858 1 12 1ZM7.25 9.5C6.2835 9.5 5.5 10.2835 5.5 11.25V18.75C5.5 19.7165 6.2835 20.5 7.25 20.5H16.75C17.7165 20.5 18.5 19.7165 18.5 18.75V11.25C18.5 10.2835 17.7165 9.5 16.75 9.5H7.25ZM12 13.75C12.6904 13.75 13.25 14.3096 13.25 15C13.25 15.6904 12.6904 16.25 12 16.25C11.3096 16.25 10.75 15.6904 10.75 15C10.75 14.3096 11.3096 13.75 12 13.75ZM12 2.5C10.067 2.5 8.5 4.067 8.5 6V8H15.5V6C15.5 4.067 13.933 2.5 12 2.5Z"/></svg>
    {{footer}}
  </footer>
</main>
</body>
</html>
""";
    }
}
