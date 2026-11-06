using System.Windows.Automation;
using CodexLanguageFix.Contracts;
using CodexLanguageFix.Core;
using CodexLanguageFix.Windows;

namespace CodexLanguageFix.Tests;

public sealed class GenericWindowsFieldTests
{
    [Theory]
    [InlineData("Edit", true)]
    [InlineData("TextBox", true)]
    [InlineData("WindowsForms10.EDIT.app.0.123", true)]
    [InlineData("inputarea", false)]
    [InlineData("monaco-editor", false)]
    [InlineData("", false)]
    public void UnknownEditorImplementationsAreNotAssumedWritable(string name, bool expected) =>
        Assert.Equal(expected, CodexComposerAccessor.IsWindowsTextField(ControlType.Edit, name, hasWritableValue: false));

    [Theory]
    [InlineData("TextBox")]
    [InlineData("Windows.UI.Xaml.Controls.TextBox")]
    [InlineData("QLineEdit")]
    [InlineData("")]
    public void WritableValueContractSupportsEditControlsWithoutClassAllowlist(string name) =>
        Assert.True(CodexComposerAccessor.IsWindowsTextField(ControlType.Edit, name, hasWritableValue: true));

    [Fact]
    public void WritableValueDoesNotTurnUnrelatedControlsIntoTextFields()
    {
        Assert.False(CodexComposerAccessor.IsWindowsTextField(ControlType.Spinner, "", hasWritableValue: true));
        Assert.False(CodexComposerAccessor.IsWindowsTextField(ControlType.Document, "RootWebArea", hasWritableValue: true));
        Assert.False(NaturalLanguageField.IsSupported(ComposerHost.Windows, "monaco-editor", "description", null, null));
    }

    [Theory]
    [InlineData("RichEditD2DPT")]
    [InlineData("RichTextBox")]
    public void NativeRichTextDocumentsCanBeDetected(string name) =>
        Assert.True(CodexComposerAccessor.IsNativeRichText(ControlType.Document, name));

    [Theory]
    [InlineData("password")]
    [InlineData("creditcard")]
    [InlineData("otp")]
    [InlineData("login")]
    [InlineData("command")]
    public void SensitiveMetadataIsExcluded(string id) =>
        Assert.False(NaturalLanguageField.IsSupported(ComposerHost.Windows, "Edit", id, null, null));

    [Fact]
    public void OrdinaryProseIsSupportedButCodeAndRootDocumentsAreNot()
    {
        Assert.True(NaturalLanguageField.IsSupported(ComposerHost.Windows, "Edit", "description", "Beschreibung", null));
        Assert.False(NaturalLanguageField.IsSupported(ComposerHost.Windows, "monaco-editor", "description", null, null));
        Assert.False(CodexComposerAccessor.IsNativeRichText(ControlType.Document, "RootWebArea"));
    }
}
