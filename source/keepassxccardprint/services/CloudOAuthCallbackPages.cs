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
        var checkPath = succeeded
            ? "M6 12.5l4.3 4.3L18.5 8.5"
            : "M12 7.5v5.5m0 4h.01";
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
      border-radius:50%; color:{{accent}}; border:1px solid {{accent}};
      background:{{surface2}}; }
    .status { font-size:11px; font-weight:750; letter-spacing:1.5px; color:{{accent}}; }
    h1 { font-size:24px; letter-spacing:-.65px; line-height:1.25; margin:0 0 12px; }
    p { font-size:14px; color:{{subtle}}; line-height:1.65; margin:0; }
    .hint { margin-top:25px; padding:14px 16px; border:1px solid {{outline}};
      background:{{surface2}}; border-radius:10px; font-size:13px; color:{{text}}; }
    footer { display:flex; align-items:center; gap:8px; margin-top:23px;
      color:{{subtle}}; font-size:11px; }
    footer svg { color:{{accent}}; flex-shrink:0; }
    @media(max-width:540px) { main { padding:26px 22px 22px; } .rule { margin-left:-22px; margin-right:-22px; } h1 { font-size:21px; } }
  </style>
</head>
<body>
<main role="main">
  <div class="brand">
    <div class="appmark" aria-hidden="true">
      <svg width="31" height="32" viewBox="0 0 32 32" fill="none" stroke="currentColor" stroke-width="1.65">
        <path d="M7 3.5h13l6 6V28H7z" stroke-linejoin="round"/>
        <path d="M20 3.5V10h6"/>
        <circle cx="14" cy="18" r="3.2"/>
        <path d="M17.2 18H24m-2 0v3m-3-3v2" stroke-linecap="round"/>
      </svg>
    </div>
    <div><div class="brandname">KeePassXC Card Print</div><div class="brandinfo">Google Drive · OAuth 2.0</div></div>
  </div>
  <div class="rule"></div>
  <div class="result">
    <div class="resulticon" aria-hidden="true">
      <svg width="25" height="25" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.3" stroke-linecap="round" stroke-linejoin="round"><path d="{{checkPath}}"/></svg>
    </div>
    <span class="status">{{state}}</span>
  </div>
  <h1>{{headline}}</h1>
  <p>{{description}}</p>
  <div class="hint">{{instruction}}</div>
  <footer>
    <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6"><rect x="5" y="10" width="14" height="11" rx="2"/><path d="M8 10V7a4 4 0 0 1 8 0v3"/></svg>
    {{footer}}
  </footer>
</main>
</body>
</html>
""";
    }
}
