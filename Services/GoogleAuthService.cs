#if ANDROID

using Android.Gms.Common.Util.Concurrent;
using Android.OS;
using AndroidX.Credentials;
using Google.Android.Libraries.Identity.GoogleId;
using Microsoft.Maui.ApplicationModel;
using System.Text;
using System.Text.Json;
using Exception = System.Exception;
using Object = Java.Lang.Object;

namespace HisabKitab.Services;

public class GoogleAuthService : Object, ICredentialManagerCallback
{
    private const string WebClientId =
        "505890759405-e8ag6bvf3iog9b1anikn6hkce18qn3pi.apps.googleusercontent.com";

    private TaskCompletionSource<bool>? _signInTask;

    public Task<bool> SignInAsync()
    {
        if (_signInTask != null)
            throw new InvalidOperationException(
                "Google sign-in is already in progress.");

        _signInTask = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        try
        {
            var activity = Platform.CurrentActivity;

            if (activity == null)
                throw new InvalidOperationException(
                    "Current Android Activity was not found.");

            var googleOption =
                new GetSignInWithGoogleOption.Builder(WebClientId)
                    .Build();

            var request =
                new GetCredentialRequest.Builder()
                    .AddCredentialOption(googleOption)
                    .Build();

            var mainLooper = Looper.MainLooper;

            if (mainLooper == null)
                throw new InvalidOperationException(
                    "Android MainLooper was not found.");

            var executor = new HandlerExecutor(mainLooper);

            var credentialManager =
                CredentialManager.Create(activity);

            credentialManager.GetCredentialAsync(
                activity,
                request,
                null,
                executor,
                this);
        }
        catch (Exception ex)
        {
            _signInTask.TrySetException(ex);
            _signInTask = null;
        }

        return _signInTask.Task;
    }

    public void OnResult(Object? result)
    {
        try
        {
            if (result is not GetCredentialResponse response)
            {
                throw new InvalidOperationException(
                    "Invalid credential response received.");
            }

            var credential = response.Credential;

            if (credential == null)
            {
                throw new InvalidOperationException(
                    "Google credential was not returned.");
            }

            // Convert the Android credential into Google's ID Token credential.
            var googleCredential =
                GoogleIdTokenCredential.CreateFrom(credential.Data);

            // IMPORTANT:
            // We are intentionally using IdToken here.
            // Your installed .NET binding does not expose
            // Email/UniqueId the way the Kotlin API does.
            string idToken = googleCredential.IdToken;

            if (string.IsNullOrWhiteSpace(idToken))
            {
                throw new InvalidOperationException(
                    "Google ID token was not returned.");
            }

            // DisplayName is available in your current binding.
            string? displayName = googleCredential.DisplayName;

            // Read sub/email/name from JWT payload.
            var userInfo = ReadGoogleToken(idToken);

            string googleId = userInfo.GoogleId;

            string? email = userInfo.Email;

            string name =
                !string.IsNullOrWhiteSpace(displayName)
                    ? displayName
                    : userInfo.Name ?? email ?? "Google User";

            if (string.IsNullOrWhiteSpace(googleId))
            {
                throw new InvalidOperationException(
                    "Google account ID (sub) was not returned.");
            }

            if (string.IsNullOrWhiteSpace(email))
            {
                throw new InvalidOperationException(
                    "Google email was not returned.");
            }

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                try
                {
                    await SessionService.SaveSessionAsync(
                        googleId,
                        email,
                        name);

                    _signInTask?.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    _signInTask?.TrySetException(ex);
                }
                finally
                {
                    _signInTask = null;
                }
            });
        }
        catch (Exception ex)
        {
            _signInTask?.TrySetException(ex);
            _signInTask = null;
        }
    }

    public void OnError(Java.Lang.Object error)
    {
        var message = error?.ToString() ?? "Unknown Google Sign-In error";

        System.Diagnostics.Debug.WriteLine(
            "GOOGLE SIGN-IN ERROR: " + message);

        _signInTask?.TrySetException(
            new InvalidOperationException(message));

        _signInTask = null;
    }

    private static GoogleUserInfo ReadGoogleToken(string idToken)
    {
        try
        {
            var parts = idToken.Split('.');

            if (parts.Length < 2)
                throw new InvalidOperationException(
                    "Invalid Google ID token format.");

            string payload = parts[1];

            // Base64Url → Base64
            payload = payload
                .Replace('-', '+')
                .Replace('_', '/');

            switch (payload.Length % 4)
            {
                case 2:
                    payload += "==";
                    break;

                case 3:
                    payload += "=";
                    break;
            }

            byte[] bytes = Convert.FromBase64String(payload);

            string json = Encoding.UTF8.GetString(bytes);

            using var document =
                JsonDocument.Parse(json);

            var root = document.RootElement;

            string googleId =
                GetJsonString(root, "sub") ?? string.Empty;

            string? email =
                GetJsonString(root, "email");

            string? name =
                GetJsonString(root, "name");

            return new GoogleUserInfo(
                googleId,
                email,
                name);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Unable to read Google account information from ID token.",
                ex);
        }
    }

    private static string? GetJsonString(
        JsonElement element,
        string propertyName)
    {
        if (!element.TryGetProperty(
                propertyName,
                out var property))
        {
            return null;
        }

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private sealed record GoogleUserInfo(
        string GoogleId,
        string? Email,
        string? Name);
}

#else

namespace HisabKitab.Services;

public class GoogleAuthService
{
    public Task<bool> SignInAsync()
    {
        return Task.FromResult(false);
    }
}

#endif