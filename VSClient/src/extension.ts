/* --------------------------------------------------------------------------------------------
 * Copyright (c) Microsoft Corporation. All rights reserved.
 * Licensed under the MIT License. See License.txt in the project root for license information.
 * ------------------------------------------------------------------------------------------ */
'use strict';

import * as path from 'path';
import * as os from 'os';
import { workspace, ExtensionContext,  commands, window, Terminal } from 'vscode';

import {
	LanguageClient,
	LanguageClientOptions,
	ServerOptions,
	ExecutableOptions,
	Executable,
	TransportKind
} from 'vscode-languageclient/node';

let client: LanguageClient;

export function activate(context: ExtensionContext) {

	const command = 'leafLanguageExtension.compile';

  	const commandHandler = (outputFileName:string = 'out.exe') => {
    	let filePath: string = window.activeTextEditor.document.uri.fsPath;
		let terminal: Terminal = window.createTerminal('sendText', "cmd");
		let compileCommand = context.asAbsolutePath(path.join('server', 'Language.Experimental.Compiler.exe'));
        terminal.sendText(`"${compileCommand}" -i "${filePath}" -o "${outputFileName}" -q`);
        terminal.show();
  	};
	
    context.subscriptions.push(commands.registerCommand(command, commandHandler));


	// The server is implemented in C#
	let serverCommand = context.asAbsolutePath(path.join('server', 'Leaf.LSP.Server.exe'));
	let commandOptions: ExecutableOptions = { env: process.env, detached: false };
	
	// If the extension is launched in debug mode then the debug server options are used
	// Otherwise the run options are used
	let serverOptions: ServerOptions =
		(os.platform() === 'win32') ? {
			run : <Executable>{ command: serverCommand, options: commandOptions, transport: TransportKind.stdio },
			debug: <Executable>{ command: serverCommand, options: commandOptions, transport: TransportKind.stdio }
		} : {
			run : <Executable>{ command: 'mono', args: [serverCommand], options: commandOptions },
			debug: <Executable>{ command: 'mono', args: [serverCommand], options: commandOptions }
		};

	// Options to control the language client
	let clientOptions: LanguageClientOptions = {
		// Register the server for plain text documents
		documentSelector: ['leaf'],
		synchronize: {
			// Synchronize the setting section 'languageServerExample' to the server
			configurationSection: 'languageServerExample',
			// Notify the server about file changes to '.clientrc files contain in the workspace
			fileEvents: workspace.createFileSystemWatcher('**/.clientrc')
		},
		
	};
	
	// Create the language client and start the client.
	client = new LanguageClient(
		'leafLanguageServer',
		'Leaf Language Server',
		serverOptions,
		clientOptions
	);
	
	// Start the client. This will also launch the server
	client.start();
}

export function deactivate(): Thenable<void> {
	if (!client) {
		return undefined;
	}
	return client.stop();
}
