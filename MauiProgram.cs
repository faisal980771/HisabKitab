using CwSoftware.Papiro;
using HisabKitab.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Handlers;

namespace HisabKitab
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();

#if ANDROID

            EntryHandler.Mapper.AppendToMapping(
                "HisabKitabCursor",
                (handler, view) =>
                {
                    if (Android.OS.Build.VERSION.SdkInt >=
                        Android.OS.BuildVersionCodes.Q)
                    {
                        var cursor =
                            new Android.Graphics.Drawables.GradientDrawable();

                        cursor.SetColor(
                            Android.Graphics.Color.ParseColor("#718238"));

                        cursor.SetSize(5, -1);

                        handler.PlatformView.TextCursorDrawable = cursor;
                    }
                });

            EditorHandler.Mapper.AppendToMapping(
                "HisabKitabEditorCursor",
                (handler, view) =>
                {
                    if (Android.OS.Build.VERSION.SdkInt >=
                        Android.OS.BuildVersionCodes.Q)
                    {
                        var cursor =
                            new Android.Graphics.Drawables.GradientDrawable();

                        cursor.SetColor(
                            Android.Graphics.Color.ParseColor("#718238"));

                        cursor.SetSize(5, -1);

                        handler.PlatformView.TextCursorDrawable = cursor;
                    }
                });

#endif
            builder
                .UseMauiApp<App>()
                .UsePapiro()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");

                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                });

#if DEBUG
            builder.Logging.AddDebug();
#endif

            var app = builder.Build();
            BillPdfService.Initialize(app.Services.GetRequiredService<IHtmlToPdfService>());
            return app;
        }
    }
}
