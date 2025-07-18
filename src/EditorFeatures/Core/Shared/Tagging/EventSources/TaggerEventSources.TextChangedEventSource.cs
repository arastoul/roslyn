﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.Text;

namespace Microsoft.CodeAnalysis.Editor.Shared.Tagging;

internal partial class TaggerEventSources
{
    private sealed class TextChangedEventSource : AbstractTaggerEventSource
    {
        private readonly ITextBuffer _subjectBuffer;

        public TextChangedEventSource(ITextBuffer subjectBuffer)
        {
            Contract.ThrowIfNull(subjectBuffer);
            _subjectBuffer = subjectBuffer;
        }

        public override void Connect()
        {
            if (_subjectBuffer is ITextBuffer2 buffer2)
                buffer2.ChangedOnBackground += OnTextBufferChanged;
            else
                _subjectBuffer.Changed += OnTextBufferChanged;
        }

        public override void Disconnect()
        {
            if (_subjectBuffer is ITextBuffer2 buffer2)
                buffer2.ChangedOnBackground -= OnTextBufferChanged;
            else
                _subjectBuffer.Changed -= OnTextBufferChanged;
        }

        private void OnTextBufferChanged(object? sender, TextContentChangedEventArgs e)
        {
            if (e.Changes.Count == 0)
                return;

            this.RaiseChanged();
        }
    }
}
