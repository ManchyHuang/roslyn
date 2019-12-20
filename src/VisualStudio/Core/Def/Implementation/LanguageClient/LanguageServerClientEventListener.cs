﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Composition;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Editor;
using Microsoft.CodeAnalysis.Host;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.VisualStudio.LanguageServer.Client;
using Microsoft.VisualStudio.Threading;

namespace Microsoft.VisualStudio.LanguageServices.Implementation.LanguageService
{
    // unfortunately, we can't implement this on LanguageServerClient since this uses MEF v2 and
    // ILanguageClient requires MEF v1 and two can't be mixed exported in 1 class.
    [Export]
    [ExportEventListener(WellKnownEventListeners.Workspace, WorkspaceKind.Host), Shared]
    internal class LanguageServerClientEventListener : IEventListener<object>
    {
        private readonly LanguageServerClient _languageServerClient;
        private readonly Lazy<ILanguageClientBroker> _languageClientBroker;
        private readonly TaskCompletionSource<object> _taskCompletionSource;

        public Task WorkspaceStarted => _taskCompletionSource.Task;

        [ImportingConstructor]
        [Obsolete(MefConstruction.ImportingConstructorMessage, error: true)]
        public LanguageServerClientEventListener(LanguageServerClient languageServerClient, Lazy<ILanguageClientBroker> languageClientBroker)
        {
            this._languageServerClient = languageServerClient;
            this._languageClientBroker = languageClientBroker;
            this._taskCompletionSource = new TaskCompletionSource<object>();
        }

        /// <summary>
        /// LSP clients do not necessarily know which language servers (and when) to activate as they are language agnostic.
        /// We know we can provide <see cref="LanguageServerClient"/> as soon as the workspace is started, so tell the
        /// <see cref="ILanguageClientBroker"/> to start loading it.
        /// </summary>
        public void StartListening(Workspace workspace, object serviceOpt)
        {
            // mark that roslyn solution is added
            this._taskCompletionSource.SetResult(null);

            this.WorkspaceStarted.ContinueWith(_ =>
            {
                // Trigger a fire and forget request to the VS LSP client to load our ILanguageClient.
                // This needs to be done with .Forget() as the LoadAsync (VS LSP client) synchronously stores the result task of OnLoadedAsync.
                // The synchronous execution happens under the sln load threaded wait dialog, so user actions cannot be made in between triggering LoadAsync and storing the result task from OnLoadedAsync.
                // The result task from OnLoadedAsync is waited on before invoking LSP requests to the ILanguageClient.
                this._languageClientBroker.Value.LoadAsync(new LanguageClientMetadata(new string[] { ContentTypeNames.CSharpContentType, ContentTypeNames.VisualBasicContentType }), this._languageServerClient).Forget();
            }, TaskScheduler.Default);
        }

        /// <summary>
        /// The <see cref="ILanguageClientBroker.LoadAsync(ILanguageClientMetadata, ILanguageClient)"/> 
        /// requires that we pass the <see cref="ILanguageClientMetadata"/> along with the language client instance.
        /// The implementation of <see cref="ILanguageClientMetadata"/> is not public, so have to re-implement.
        /// https://devdiv.visualstudio.com/DevDiv/_workitems/edit/1043922 tracking to remove this.
        /// </summary>
        private class LanguageClientMetadata : ILanguageClientMetadata
        {
            public LanguageClientMetadata(string[] contentTypes, string clientName = null)
            {
                this.ContentTypes = contentTypes;
                this.ClientName = clientName;
            }

            public string ClientName { get; }

            public IEnumerable<string> ContentTypes { get; }
        }
    }
}
