using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace CitrineLauncher
{
    public partial class MainWindow
    {
        private async void StartParticleAnimation()
        {
            if (_particleCts != null)
                return;

            _particleCts = new CancellationTokenSource();
            var token = _particleCts.Token;
            try
            {
                while (!token.IsCancellationRequested)
                {
                    if (Handlers.Settings.Instance.ParticlesEnabled)
                    {
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            // Bug fix #5: cap particle count to prevent unbounded accumulation
                            if (ParticleCanvas.Children.Count >= 80) return;

                            var particle = new Avalonia.Controls.Shapes.Ellipse
                            {
                                Width = _random.Next(2, 5),
                                Height = _random.Next(2, 5),
                                Fill = new SolidColorBrush(Color.FromArgb((byte)_random.Next(100, 200), 226, 176, 7)),
                                Opacity = 0.8
                            };
                            double x = ParticleCanvas.Bounds.Width > 0 ? _random.Next(0, (int)ParticleCanvas.Bounds.Width) : _random.Next(100, 800);
                            double y = ParticleCanvas.Bounds.Height > 0 ? _random.Next(0, (int)ParticleCanvas.Bounds.Height) : _random.Next(100, 400);
                            Canvas.SetLeft(particle, x);
                            Canvas.SetTop(particle, y);
                            ParticleCanvas.Children.Add(particle);
                            _ = FadeOutAndRemove(particle, token);
                        }, DispatcherPriority.Background);
                    }

                    // Improvement #12: use ParticleDensity to control spawn rate
                    // Density 100 = fast (100ms delay), Density 0 = slow (600ms delay)
                    int density = Handlers.Settings.Instance.ParticleDensity;
                    int delay = 600 - (int)(density / 100.0 * 500) + _random.Next(0, 100);
                    await Task.Delay(delay, token);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task FadeOutAndRemove(Avalonia.Controls.Shapes.Ellipse particle, CancellationToken token)
        {
            try
            {
                await Task.Delay(3000, token);
                for (double opacity = 0.8; opacity > 0; opacity -= 0.1)
                {
                    if (token.IsCancellationRequested) break;
                    await Dispatcher.UIThread.InvokeAsync(() => particle.Opacity = opacity, DispatcherPriority.Background);
                    await Task.Delay(50, token);
                }
                await Dispatcher.UIThread.InvokeAsync(() => ParticleCanvas.Children.Remove(particle), DispatcherPriority.Background);
            }
            catch
            {
                await Dispatcher.UIThread.InvokeAsync(() => ParticleCanvas.Children.Remove(particle), DispatcherPriority.Background);
            }
        }

        private void ToggleParticlesButton_Click(object? sender, RoutedEventArgs e)
        {
            var settings = Handlers.Settings.Instance;
            settings.ParticlesEnabled = !settings.ParticlesEnabled;
            ToggleParticlesButton.Opacity = settings.ParticlesEnabled ? 1.0 : 0.5;
            if (!settings.ParticlesEnabled) ParticleCanvas.Children.Clear();
        }
    }
}
