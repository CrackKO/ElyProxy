using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ElyProxy.Models;
using ElyProxy.Services;
using Microsoft.Win32;
using QRCoder;

namespace ElyProxy.Views.Sheets;

public partial class ShareServerSheet : UserControl, INotifyPropertyChanged
{
    public ShareServerSheet(VlessNode node)
    {
        InitializeComponent();
        Link = ShareLinkBuilder.Build(node);
        ProtocolNote = string.IsNullOrWhiteSpace(Link)
            ? $"Для протокола {node.ProtocolDisplay} ссылка-импорт не поддерживается; используйте экспорт профиля в Clash YAML или JSON."
            : $"Протокол: {node.ProtocolDisplay}. Ссылка подходит для Happ, v2rayN, Hiddify и других клиентов.";
        if (!string.IsNullOrWhiteSpace(Link))
        {
            try
            {
                using QRCodeGenerator generator = new QRCodeGenerator();
                using QRCodeData data = generator.CreateQrCode(Link, QRCodeGenerator.ECCLevel.M);
                byte[] png = new PngByteQRCode(data).GetGraphic(6);
                BitmapImage image = new BitmapImage();
                using MemoryStream stream = new MemoryStream(png);
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
                image.Freeze();
                QrImage.Source = image;
                HasQr = true;
            }
            catch
            {
                HasQr = false;
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Link { get; }

    public string ProtocolNote { get; }

    public bool HasQr { get; }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Link))
        {
            return;
        }
        try
        {
            Clipboard.SetText(Link);
            DialogService.Current.Notify("Ссылка скопирована", "Вставьте её в другой клиент для импорта.", NoticeKind.Success);
        }
        catch (Exception ex)
        {
            DialogService.Current.Notify("Не удалось скопировать", ex.Message, NoticeKind.Error);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Link))
        {
            return;
        }
        SaveFileDialog dialog = new SaveFileDialog
        {
            Title = "Сохранить ссылку",
            FileName = "elyproxy-server.txt",
            Filter = "Текстовый файл (*.txt)|*.txt|Все файлы (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        try
        {
            File.WriteAllText(dialog.FileName, Link);
            DialogService.Current.Notify("Ссылка сохранена", dialog.FileName, NoticeKind.Success);
        }
        catch (Exception ex)
        {
            DialogService.Current.Notify("Не удалось сохранить", ex.Message, NoticeKind.Error);
        }
    }
}
