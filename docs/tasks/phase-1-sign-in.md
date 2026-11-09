# Phase 1 - Sign in

Cards 1.1-1.3 and 1.6 are done: `src/Guit.Api/Auth/`, `src/Guit.Core/Services/Session.cs`,
`src/Guit.Core/ViewModels/SignInViewModel.cs`, `src/Guit.App/Views/SignInView.xaml`,
`src/Guit.App/Services/`.

How sign-in works (GitHub device flow, no client secret):

1. `POST https://github.com/login/device/code` with the client id and scopes -> a `user_code` and a `device_code`.
2. The app shows the `user_code`, copies it to the clipboard, and opens `https://github.com/login/device` in the browser.
3. The app polls `POST https://github.com/login/oauth/access_token` every `interval` seconds until the member approves.
4. The token goes to `Session.SignIn`, which stores it in the Windows Credential Locker.

---

## 1.4 [H] Register the OAuth App and sign in for real

**Who**: a human with a GitHub account and a Windows 10/11 machine.

1. GitHub -> Settings -> Developer settings -> OAuth Apps -> New OAuth App.
   Name: `Guit (dev)`. Homepage URL: the repository URL. Callback URL: `http://127.0.0.1` (unused by the device flow, but required).
2. On the app's page tick **Enable Device Flow** and save. Copy the **Client ID**. Do not create a client secret.
3. Put the client id in `src/Guit.App/AppConfig.cs` (replace `REPLACE_WITH_OAUTH_APP_CLIENT_ID`), or set the
   `GUIT_GITHUB_CLIENT_ID` environment variable. A client id is public; committing it is fine.
4. `dotnet publish src/Guit.App -c Release -r win-x64 -p:Platform=x64`, run `artifacts\publish\Guit.App\release_win-x64\Guit.exe`.
5. Check and write the results into the pull request or an issue:
   - [ ] the window opens with Mica and a title bar
   - [ ] "Sign in with GitHub" shows a code, the browser opens, the code is on the clipboard
   - [ ] after approving, the navigation view appears with your login and avatar in the footer
   - [ ] close and reopen: still signed in
   - [ ] right-click the account item -> Sign out returns to the sign-in view
   - [ ] start-up time, using the command in the `performance` skill

Nobody has run the app interactively yet. CI proves it compiles, publishes with Native AOT, starts and creates a
window; it cannot click "Sign in". Expect to find small problems here; report them rather than patching around them.

---

## 1.5 [S] Sign out automatically on HTTP 401

**Layer** Api + Core + App - **Skills** `aot-rules`, `viewmodel`, `winui-xaml` - **Depends on** 1.4

A revoked or expired token must drop the app back to the sign-in view instead of failing every request.

**Why this is [S]**: the 401 is seen on a thread-pool thread inside an HTTP handler, and `Session.Changed`
updates view models, which must happen on the UI thread. Getting that wrong crashes only under real use.

**Build**
- Core: `Services/IUiDispatcher.cs` with `void Post(Action action)`. App implements it with
  `DispatcherQueue.TryEnqueue` (capture `DispatcherQueue.GetForCurrentThread()` in `App.OnLaunched`).
  Tests and AotSmoke use an inline implementation that just invokes the action.
- Api: `Auth/IUnauthorizedHandler.cs` with `void OnUnauthorized()`, and `Http/UnauthorizedDelegatingHandler`
  (a `DelegatingHandler`) that calls it when a reply has status 401. Add it in `AddGitHubApi<T>` with
  `.AddHttpMessageHandler(...)`. It must not be added to the device-flow client.
- Core: `Session` implements `IUnauthorizedHandler`; `OnUnauthorized` posts `SignOut` through `IUiDispatcher`,
  and does nothing if already signed out. Register `IUnauthorizedHandler` -> `Session` in `AddGuitCore`.

**Tests**: handler calls the listener on 401 and not on 200/403/404 (Api tests, with `StubHttp`);
`Session.OnUnauthorized` clears the token store and raises `Changed` once (Core tests).
Add a smoke `Check`: a canned 401 on one path signs the session out.

---

## 1.6 Friendly error messages (done)

`src/Guit.Core/Services/ApiErrors.cs`: `ApiErrors.Describe(exception)` maps a failed call to a sentence.
View models catch `Refit.ApiExceptionBase` and call it. See the `viewmodel` skill.
