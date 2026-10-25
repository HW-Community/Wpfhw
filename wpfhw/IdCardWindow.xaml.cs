using System.Text;
using System.Windows;
using System.Windows.Input;

namespace wpfhw;

public partial class IdCardWindow : Window
{
    private readonly CancellationTokenSource _cancellation = new();
    private bool _busy;

    public IdCardWindow()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
        };
    }

    private async void BtnValidate_Click(object sender, RoutedEventArgs e)
        => await ValidateAsync();

    private async void TxtIdCard_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await ValidateAsync();
        }
    }

    private async Task ValidateAsync()
    {
        if (_busy) return;

        _busy = true;
        btnValidate.IsEnabled = false;
        btnValidate.Content = "校验中…";
        txtResult.Text = "正在校验，本地无区划时最多等待约 6 秒联网查询…";

        try
        {
            IdCardValidationResult result = await IdCardHelper.ValidateAsync(txtIdCard.Text, _cancellation.Token);

            if (!_cancellation.IsCancellationRequested)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    RenderResult(result);
                    ShowAgePrompt(result);
                });
            }
        }
        catch
        {
            if (!_cancellation.IsCancellationRequested)
            {
                await Dispatcher.InvokeAsync(() =>
                    txtResult.Text = "校验失败：程序内部错误，请稍后重试。");
            }
        }
        finally
        {
            if (!_cancellation.IsCancellationRequested)
            {
                _busy = false;
                btnValidate.IsEnabled = true;
                btnValidate.Content = "开始校验";
            }
        }
    }

    private void RenderResult(IdCardValidationResult result)
    {
        var text = new StringBuilder();
        text.AppendLine($"号码是否合法：{(result.IsValid ? "合法" : "不合法")}");

        if (!result.IsValid && !string.IsNullOrWhiteSpace(result.FailureReason))
            text.AppendLine($"原因：{result.FailureReason}");

        text.AppendLine($"状态标记：{result.RegionStatusText}");

        if (!string.IsNullOrWhiteSpace(result.RegionStatusDetail))
            text.AppendLine($"提示：{result.RegionStatusDetail}");

        text.AppendLine($"6位区划码：{ValueOrDash(result.RegionCode)}");
        text.AppendLine($"归属地名称：{ValueOrDash(result.RegionName)}");
        text.AppendLine($"出生日期：{result.BirthDate?.ToString("yyyy-MM-dd") ?? "—"}");
        text.AppendLine($"周岁：{result.Age?.ToString() ?? "—"}");
        text.AppendLine($"性别：{ValueOrDash(result.Gender)}");
        text.AppendLine($"年龄状态：{(result.IsAdult is null ? "—" : result.IsAdult.Value ? "成年" : "未成年")}");

        txtResult.Text = text.ToString().TrimEnd();
    }

    private void ShowAgePrompt(IdCardValidationResult result)
    {
        if (!result.IsValid || result.Age is null) return;

        if (result.Age < 18)
        {
            MessageBox.Show(this,
                "未满18周岁，不允许使用本软件",
                "年龄提示",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else
        {
            MessageBox.Show(this,
                "已满18周岁，可以使用本软件。",
                "年龄提示",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    private static string ValueOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
