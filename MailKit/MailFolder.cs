﻿//
// MailFolder.cs
//
// Author: Jeffrey Stedfast <jestedfa@microsoft.com>
//
// Copyright (c) 2013-2025 .NET Foundation and Contributors
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//

using System;
using System.IO;
using System.Threading;
using System.Collections;
using System.Threading.Tasks;
using System.Collections.Generic;

using MimeKit;

using MailKit.Search;

#if NET5_0_OR_GREATER
using IReadOnlySetOfStrings = System.Collections.Generic.IReadOnlySet<string>;
#else
using IReadOnlySetOfStrings = System.Collections.Generic.ISet<string>;
#endif

namespace MailKit {
	/// <summary>
	/// An abstract mail folder implementation.
	/// </summary>
	/// <remarks>
	/// An abstract mail folder implementation.
	/// </remarks>
	public abstract class MailFolder : IMailFolder
	{
		/// <summary>
		/// The bit mask of settable flags.
		/// </summary>
		/// <remarks>
		/// Only flags in the list of settable flags may be set on a message by the client.
		/// </remarks>
		protected static readonly MessageFlags SettableFlags = MessageFlags.Answered | MessageFlags.Deleted |
			MessageFlags.Draft | MessageFlags.Flagged | MessageFlags.Seen;

		IMailFolder parent;

		/// <summary>
		/// Initialize a new instance of the <see cref="MailKit.MailFolder"/> class.
		/// </summary>
		/// <remarks>
		/// Initializes a new instance of the <see cref="MailKit.MailFolder"/> class.
		/// </remarks>
		protected MailFolder ()
		{
			FirstUnread = -1;
		}

		/// <summary>
		/// Get an object that can be used to synchronize access to the folder.
		/// </summary>
		/// <remarks>
		/// <para>Gets an object that can be used to synchronize access to the folder.</para>
		/// </remarks>
		/// <value>The sync root.</value>
		public abstract object SyncRoot {
			get;
		}

		/// <summary>
		/// Get the parent folder.
		/// </summary>
		/// <remarks>
		/// Root-level folders do not have a parent folder.
		/// </remarks>
		/// <value>The parent folder.</value>
		public IMailFolder ParentFolder {
			get { return parent; }
			internal protected set {
				if (value == parent)
					return;

				if (parent != null)
					parent.Renamed -= OnParentFolderRenamed;

				parent = value;

				if (parent != null)
					parent.Renamed += OnParentFolderRenamed;
			}
		}

		/// <summary>
		/// Get the folder attributes.
		/// </summary>
		/// <remarks>
		/// Gets the folder attributes.
		/// </remarks>
		/// <value>The folder attributes.</value>
		public FolderAttributes Attributes {
			get; internal protected set;
		}

		/// <summary>
		/// Get the annotation access level.
		/// </summary>
		/// <remarks>
		/// If annotations are supported, this property can be used to determine whether or not
		/// the <see cref="IMailFolder"/> supports reading and writing annotations.
		/// </remarks>
		/// <value>The annotation access level.</value>
		public AnnotationAccess AnnotationAccess {
			get; internal protected set;
		}

		/// <summary>
		/// Get the supported annotation scopes.
		/// </summary>
		/// <remarks>
		/// If annotations are supported, this property can be used to determine which
		/// annotation scopes are supported by the <see cref="IMailFolder"/>.
		/// </remarks>
		/// <value>The supported annotation scopes.</value>
		public AnnotationScope AnnotationScopes {
			get; internal protected set;
		}

		/// <summary>
		/// Get the maximum size of annotation values supported by the folder.
		/// </summary>
		/// <remarks>
		/// If annotations are supported, this property can be used to determine the
		/// maximum size of annotation values supported by the <see cref="IMailFolder"/>.
		/// </remarks>
		/// <value>The maximum size of annotation values supported by the folder.</value>
		public uint MaxAnnotationSize {
			get; internal protected set;
		}

		/// <summary>
		/// Get the permanent flags.
		/// </summary>
		/// <remarks>
		/// <para>The permanent flags are the message flags that will persist between sessions.</para>
		/// <para>If the <see cref="MessageFlags.UserDefined"/> flag is set, then the folder allows
		/// storing of user-defined keywords.</para>
		/// </remarks>
		/// <value>The permanent flags.</value>
		public MessageFlags PermanentFlags {
			get; protected set;
		}

		/// <summary>
		/// Get the permanent keywords.
		/// </summary>
		/// <remarks>
		/// <para>The permanent keywords are the keywords that will persist between sessions.</para>
		/// <para>If the <see cref="MessageFlags.UserDefined"/> flag is set in <see cref="PermanentFlags"/>,
		/// then the folder allows storing of user-defined keywords as well.</para>
		/// </remarks>
		/// <value>The permanent keywords.</value>
		public IReadOnlySetOfStrings PermanentKeywords {
			get; protected set;
		}

		/// <summary>
		/// Get the accepted flags.
		/// </summary>
		/// <remarks>
		/// The accepted flags are the message flags that will be accepted and persist
		/// for the current session. For the set of flags that will persist between
		/// sessions, see the <see cref="PermanentFlags"/> property.
		/// </remarks>
		/// <value>The accepted flags.</value>
		public MessageFlags AcceptedFlags {
			get; protected set;
		}

		/// <summary>
		/// Get the accepted keywords.
		/// </summary>
		/// <remarks>
		/// The accepted keywords are the keywords that will be accepted and persist
		/// for the current session. For the set of keywords that will persist between
		/// sessions, see the <see cref="PermanentKeywords"/> property.
		/// </remarks>
		/// <value>The accepted keywords.</value>
		public IReadOnlySetOfStrings AcceptedKeywords {
			get; protected set;
		}

		/// <summary>
		/// Get the directory separator.
		/// </summary>
		/// <remarks>
		/// Gets the directory separator.
		/// </remarks>
		/// <value>The directory separator.</value>
		public char DirectorySeparator {
			get; protected set;
		}

		/// <summary>
		/// Get the read/write access of the folder.
		/// </summary>
		/// <remarks>
		/// Gets the read/write access of the folder.
		/// </remarks>
		/// <value>The read/write access.</value>
		public FolderAccess Access {
			get; internal protected set;
		}

		/// <summary>
		/// Get whether or not the folder is a namespace folder.
		/// </summary>
		/// <remarks>
		/// Gets whether or not the folder is a namespace folder.
		/// </remarks>
		/// <value><see langword="true" /> if the folder is a namespace folder; otherwise, <see langword="false" />.</value>
		public bool IsNamespace {
			get; protected set;
		}

		/// <summary>
		/// Get the full name of the folder.
		/// </summary>
		/// <remarks>
		/// This is the equivalent of the full path of a file on a file system.
		/// </remarks>
		/// <value>The full name of the folder.</value>
		public string FullName {
			get; protected set;
		}

		/// <summary>
		/// Get the name of the folder.
		/// </summary>
		/// <remarks>
		/// This is the equivalent of the file name of a file on the file system.
		/// </remarks>
		/// <value>The name of the folder.</value>
		public string Name {
			get; protected set;
		}

		/// <summary>
		/// Get the unique identifier for the folder, if available.
		/// </summary>
		/// <remarks>
		/// <para>Gets a unique identifier for the folder, if available. This is useful for clients
		/// implementing a message cache that want to track the folder after it is renamed by another
		/// client.</para>
		/// <note type="note">This property will only be available if the server supports the
		/// <a href="https://tools.ietf.org/html/rfc8474">OBJECTID</a> extension.</note>
		/// </remarks>
		/// <value>The unique folder identifier.</value>
		public string Id {
			get; protected set;
		}

		/// <summary>
		/// Get a value indicating whether the folder is subscribed.
		/// </summary>
		/// <remarks>
		/// Gets a value indicating whether the folder is subscribed.
		/// </remarks>
		/// <value><see langword="true" /> if the folder is subscribed; otherwise, <see langword="false" />.</value>
		public bool IsSubscribed {
			get { return (Attributes & FolderAttributes.Subscribed) != 0; }
		}

		/// <summary>
		/// Get a value indicating whether the folder is currently open.
		/// </summary>
		/// <remarks>
		/// Gets a value indicating whether the folder is currently open.
		/// </remarks>
		/// <value><see langword="true" /> if the folder is currently open; otherwise, <see langword="false" />.</value>
		public abstract bool IsOpen {
			get;
		}

		/// <summary>
		/// Get whether or not the folder can be opened.
		/// </summary>
		/// <remarks>
		/// Gets whether or not the folder can be opened.
		/// </remarks>
		/// <value><see langword="true" /> if the folder can be opened; otherwise, <see langword="false" />.</value>
		public bool CanOpen {
			get { return (Attributes & (FolderAttributes.NoSelect | FolderAttributes.NonExistent)) == 0; }
		}

		/// <summary>
		/// Get a value indicating whether the folder exists.
		/// </summary>
		/// <remarks>
		/// Gets a value indicating whether the folder exists.
		/// </remarks>
		/// <value><see langword="true" /> if the folder exists; otherwise, <see langword="false" />.</value>
		public bool Exists {
			get { return (Attributes & FolderAttributes.NonExistent) == 0; }
		}

		/// <summary>
		/// Get the highest mod-sequence value of all messages in the mailbox.
		/// </summary>
		/// <remarks>
		/// Gets the highest mod-sequence value of all messages in the mailbox.
		/// </remarks>
		/// <value>The highest mod-sequence value.</value>
		public ulong HighestModSeq {
			get; protected set;
		}

		/// <summary>
		/// Get the UID validity.
		/// </summary>
		/// <remarks>
		/// <para>UIDs are only valid so long as the UID validity value remains unchanged. If and when
		/// the folder's <see cref="UidValidity"/> is changed, a client MUST discard its cache of UIDs
		/// along with any summary information that it may have and re-query the folder.</para>
		/// <para>This value will only be set after the folder has been opened.</para>
		/// </remarks>
		/// <value>The UID validity.</value>
		public uint UidValidity {
			get; protected set;
		}

		/// <summary>
		/// Get the UID that the folder will assign to the next message that is added.
		/// </summary>
		/// <remarks>
		/// This value will only be set after the folder has been opened.
		/// </remarks>
		/// <value>The next UID.</value>
		public UniqueId? UidNext {
			get; protected set;
		}

		/// <summary>
		/// Get the maximum size of a message that can be appended to the folder.
		/// </summary>
		/// <remarks>
		/// <para>Gets the maximum size of a message that can be appended to the folder.</para>
		/// <note type="note">If the value is not set, then the limit is unspecified.</note>
		/// </remarks>
		/// <value>The append limit.</value>
		public uint? AppendLimit {
			get; protected set;
		}

		/// <summary>
		/// Get the size of the folder.
		/// </summary>
		/// <remarks>
		/// <para>Gets the size of the folder in bytes.</para>
		/// <note type="note">If the value is not set, then the size is unspecified.</note>
		/// </remarks>
		/// <value>The size of the folder, in bytes.</value>
		public ulong? Size {
			get; protected set;
		}

		/// <summary>
		/// Get the index of the first unread message in the folder.
		/// </summary>
		/// <remarks>
		/// <para>Gets the index of the first unread message in the folder.</para>
		/// <note type="note"><para>This value will only be set after the folder has been opened.</para>
		/// <para>A value of <c>-1</c> indicates that there are no unread messages in the folder or that the server
		/// has not provided the index of the first unread message.</para></note>
		/// </remarks>
		/// <value>The index of the first unread message or <c>-1</c> if there are no unread messages in the folder.</value>
		public int FirstUnread {
			get; protected set;
		}

		/// <summary>
		/// Get the number of unread messages in the folder.
		/// </summary>
		/// <remarks>
		/// <para>Gets the number of unread messages in the folder.</para>
		/// <note type="note">This value will only be set after calling
		/// <see cref="Status(StatusItems, System.Threading.CancellationToken)"/>
		/// with <see cref="StatusItems.Unread"/>.</note>
		/// </remarks>
		/// <value>The number of unread messages.</value>
		public int Unread {
			get; protected set;
		}

		/// <summary>
		/// Get the number of recently delivered messages in the folder.
		/// </summary>
		/// <remarks>
		/// <para>Gets the number of recently delivered messages in the folder.</para>
		/// <note type="note">This value will only be set after calling
		/// <see cref="Status(StatusItems, System.Threading.CancellationToken)"/>
		/// with <see cref="StatusItems.Recent"/> or by opening the folder.</note>
		/// </remarks>
		/// <value>The number of recently delivered messages.</value>
		public int Recent {
			get; protected set;
		}

		/// <summary>
		/// Get the total number of messages in the folder.
		/// </summary>
		/// <remarks>
		/// <para>Gets the total number of messages in the folder.</para>
		/// <note type="note">This value will only be set after calling
		/// <see cref="Status(StatusItems, System.Threading.CancellationToken)"/>
		/// with <see cref="StatusItems.Count"/> or by opening the folder.</note>
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapExamples.cs" region="DownloadMessagesByIndex"/>
		/// </example>
		/// <value>The total number of messages.</value>
		public int Count {
			get; protected set;
		}

		/// <summary>
		/// Get the threading algorithms supported by the folder.
		/// </summary>
		/// <remarks>
		/// Gets the threading algorithms supported by the folder.
		/// </remarks>
		/// <value>The supported threading algorithms.</value>
		public abstract HashSet<ThreadingAlgorithm> ThreadingAlgorithms { get; }

		/// <summary>
		/// Determine whether or not a <see cref="MailFolder"/> supports a feature.
		/// </summary>
		/// <remarks>
		/// Determines whether or not a <see cref="MailFolder"/> supports a feature.
		/// </remarks>
		/// <param name="feature">The desired feature.</param>
		/// <returns><see langword="true" /> if the feature is supported; otherwise, <see langword="false" />.</returns>
		public abstract bool Supports (FolderFeature feature);

		/// <summary>
		/// Opens the folder using the requested folder access.
		/// </summary>
		/// <remarks>
		/// <para>This variant of the <see cref="Open(FolderAccess,System.Threading.CancellationToken)"/>
		/// method is meant for quick resynchronization of the folder. Before calling this method,
		/// the <see cref="MailStore.EnableQuickResync(CancellationToken)"/> method MUST be called.</para>
		/// <para>You should also make sure to add listeners to the <see cref="MessagesVanished"/> and
		/// <see cref="MessageFlagsChanged"/> events to get notifications of changes since
		/// the last time the folder was opened.</para>
		/// </remarks>
		/// <returns>The <see cref="FolderAccess"/> state of the folder.</returns>
		/// <param name="access">The requested folder access.</param>
		/// <param name="uidValidity">The last known <see cref="UidValidity"/> value.</param>
		/// <param name="highestModSeq">The last known <see cref="HighestModSeq"/> value.</param>
		/// <param name="uids">The last known list of unique message identifiers.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="access"/> is not a valid value.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The quick resynchronization feature has not been enabled.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the quick resynchronization feature.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract FolderAccess Open (FolderAccess access, uint uidValidity, ulong highestModSeq, IList<UniqueId> uids, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously opens the folder using the requested folder access.
		/// </summary>
		/// <remarks>
		/// <para>This variant of the <see cref="OpenAsync(FolderAccess,System.Threading.CancellationToken)"/>
		/// method is meant for quick resynchronization of the folder. Before calling this method,
		/// the <see cref="MailStore.EnableQuickResync(CancellationToken)"/> method MUST be called.</para>
		/// <para>You should also make sure to add listeners to the <see cref="MessagesVanished"/> and
		/// <see cref="MessageFlagsChanged"/> events to get notifications of changes since
		/// the last time the folder was opened.</para>
		/// </remarks>
		/// <returns>The <see cref="FolderAccess"/> state of the folder.</returns>
		/// <param name="access">The requested folder access.</param>
		/// <param name="uidValidity">The last known <see cref="UidValidity"/> value.</param>
		/// <param name="highestModSeq">The last known <see cref="HighestModSeq"/> value.</param>
		/// <param name="uids">The last known list of unique message identifiers.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="access"/> is not a valid value.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The quick resynchronization feature has not been enabled.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the quick resynchronization feature.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<FolderAccess> OpenAsync (FolderAccess access, uint uidValidity, ulong highestModSeq, IList<UniqueId> uids, CancellationToken cancellationToken = default);

		/// <summary>
		/// Open the folder using the requested folder access.
		/// </summary>
		/// <remarks>
		/// Opens the folder using the requested folder access.
		/// </remarks>
		/// <returns>The <see cref="FolderAccess"/> state of the folder.</returns>
		/// <param name="access">The requested folder access.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="access"/> is not a valid value.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract FolderAccess Open (FolderAccess access, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously open the folder using the requested folder access.
		/// </summary>
		/// <remarks>
		/// Asynchronously opens the folder using the requested folder access.
		/// </remarks>
		/// <returns>The <see cref="FolderAccess"/> state of the folder.</returns>
		/// <param name="access">The requested folder access.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="access"/> is not a valid value.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<FolderAccess> OpenAsync (FolderAccess access, CancellationToken cancellationToken = default);

		/// <summary>
		/// Close the folder, optionally expunging the messages marked for deletion.
		/// </summary>
		/// <remarks>
		/// Closes the folder, optionally expunging the messages marked for deletion.
		/// </remarks>
		/// <param name="expunge">If set to <see langword="true" />, expunge.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void Close (bool expunge = false, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously close the folder, optionally expunging the messages marked for deletion.
		/// </summary>
		/// <remarks>
		/// Asynchronously closes the folder, optionally expunging the messages marked for deletion.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="expunge">If set to <see langword="true" />, expunge.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task CloseAsync (bool expunge = false, CancellationToken cancellationToken = default);

		/// <summary>
		/// Create a new subfolder with the given name.
		/// </summary>
		/// <remarks>
		/// Creates a new subfolder with the given name.
		/// </remarks>
		/// <returns>The created folder.</returns>
		/// <param name="name">The name of the folder to create.</param>
		/// <param name="isMessageFolder"><see langword="true" /> if the folder will be used to contain messages; otherwise, <see langword="false" />.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="name"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="name"/> is empty.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="DirectorySeparator"/> is nil, and thus child folders cannot be created.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract IMailFolder Create (string name, bool isMessageFolder, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously create a new subfolder with the given name.
		/// </summary>
		/// <remarks>
		/// Asynchronously creates a new subfolder with the given name.
		/// </remarks>
		/// <returns>The created folder.</returns>
		/// <param name="name">The name of the folder to create.</param>
		/// <param name="isMessageFolder"><see langword="true" /> if the folder will be used to contain messages; otherwise, <see langword="false" />.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="name"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="name"/> is empty.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="DirectorySeparator"/> is nil, and thus child folders cannot be created.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<IMailFolder> CreateAsync (string name, bool isMessageFolder, CancellationToken cancellationToken = default);

		/// <summary>
		/// Create a new subfolder with the given name.
		/// </summary>
		/// <remarks>
		/// Creates a new subfolder with the given name.
		/// </remarks>
		/// <returns>The created folder.</returns>
		/// <param name="name">The name of the folder to create.</param>
		/// <param name="specialUses">A list of special uses for the folder being created.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="name"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="specialUses"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="name"/> is empty.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="DirectorySeparator"/> is nil, and thus child folders cannot be created.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <see cref="MailService"/> does not support the creation of special folders.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract IMailFolder Create (string name, IEnumerable<SpecialFolder> specialUses, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously create a new subfolder with the given name.
		/// </summary>
		/// <remarks>
		/// Asynchronously creates a new subfolder with the given name.
		/// </remarks>
		/// <returns>The created folder.</returns>
		/// <param name="name">The name of the folder to create.</param>
		/// <param name="specialUses">A list of special uses for the folder being created.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="name"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="specialUses"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="name"/> is empty.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="DirectorySeparator"/> is nil, and thus child folders cannot be created.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <see cref="MailService"/> does not support the creation of special folders.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<IMailFolder> CreateAsync (string name, IEnumerable<SpecialFolder> specialUses, CancellationToken cancellationToken = default);

		/// <summary>
		/// Create a new subfolder with the given name.
		/// </summary>
		/// <remarks>
		/// Creates a new subfolder with the given name.
		/// </remarks>
		/// <returns>The created folder.</returns>
		/// <param name="name">The name of the folder to create.</param>
		/// <param name="specialUse">The special use for the folder being created.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="name"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="name"/> is empty.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="DirectorySeparator"/> is nil, and thus child folders cannot be created.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <see cref="MailService"/> does not support the creation of special folders.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual IMailFolder Create (string name, SpecialFolder specialUse, CancellationToken cancellationToken = default)
		{
			return Create (name, new [] { specialUse }, cancellationToken);
		}

		/// <summary>
		/// Asynchronously create a new subfolder with the given name.
		/// </summary>
		/// <remarks>
		/// Asynchronously creates a new subfolder with the given name.
		/// </remarks>
		/// <returns>The created folder.</returns>
		/// <param name="name">The name of the folder to create.</param>
		/// <param name="specialUse">The special use for the folder being created.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="name"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="name"/> is empty.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The <see cref="DirectorySeparator"/> is nil, and thus child folders cannot be created.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <see cref="MailService"/> does not support the creation of special folders.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task<IMailFolder> CreateAsync (string name, SpecialFolder specialUse, CancellationToken cancellationToken = default)
		{
			return CreateAsync (name, new [] { specialUse }, cancellationToken);
		}

		/// <summary>
		/// Rename the folder.
		/// </summary>
		/// <remarks>
		/// Renames the folder.
		/// </remarks>
		/// <param name="parent">The new parent folder.</param>
		/// <param name="name">The new name of the folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="parent"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="name"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="parent"/> does not belong to the <see cref="IMailStore"/>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="name"/> is not a legal folder name.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The folder cannot be renamed (it is either a namespace or the Inbox).
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void Rename (IMailFolder parent, string name, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously rename the folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously renames the folder.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="parent">The new parent folder.</param>
		/// <param name="name">The new name of the folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="parent"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="name"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="parent"/> does not belong to the <see cref="IMailStore"/>.</para>
		/// <para>-or-</para>
		/// <para><paramref name="name"/> is not a legal folder name.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The folder cannot be renamed (it is either a namespace or the Inbox).
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task RenameAsync (IMailFolder parent, string name, CancellationToken cancellationToken = default);

		/// <summary>
		/// Delete the folder.
		/// </summary>
		/// <remarks>
		/// Deletes the folder.
		/// </remarks>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The folder cannot be deleted (it is either a namespace or the Inbox).
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void Delete (CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously delete the folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously deletes the folder.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// The folder cannot be deleted (it is either a namespace or the Inbox).
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task DeleteAsync (CancellationToken cancellationToken = default);

		/// <summary>
		/// Subscribe to the folder.
		/// </summary>
		/// <remarks>
		/// Subscribes to the folder.
		/// </remarks>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void Subscribe (CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously subscribe to the folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously subscribes to the folder.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task SubscribeAsync (CancellationToken cancellationToken = default);

		/// <summary>
		/// Unsubscribe from the folder.
		/// </summary>
		/// <remarks>
		/// Unsubscribes from the folder.
		/// </remarks>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void Unsubscribe (CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously unsubscribe from the folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously unsubscribes from the folder.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task UnsubscribeAsync (CancellationToken cancellationToken = default);

		/// <summary>
		/// Get the subfolders.
		/// </summary>
		/// <remarks>
		/// <para>Gets the subfolders as well as queries the server for the status of the requested items.</para>
		/// <para>When the <paramref name="items"/> argument is non-empty, this has the equivalent functionality
		/// of calling <see cref="GetSubfolders(bool,System.Threading.CancellationToken)"/> and then calling
		/// <see cref="Status(StatusItems,System.Threading.CancellationToken)"/> on each of the returned folders.</para>
		/// </remarks>
		/// <returns>The subfolders.</returns>
		/// <param name="items">The status items to pre-populate.</param>
		/// <param name="subscribedOnly">If set to <see langword="true" />, only subscribed folders will be listed.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract IList<IMailFolder> GetSubfolders (StatusItems items, bool subscribedOnly = false, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously get the subfolders.
		/// </summary>
		/// <remarks>
		/// <para>Asynchronously gets the subfolders as well as queries the server for the status of the requested items.</para>
		/// <para>When the <paramref name="items"/> argument is non-empty, this has the equivalent functionality
		/// of calling <see cref="GetSubfoldersAsync(bool,System.Threading.CancellationToken)"/> and then calling
		/// <see cref="StatusAsync(StatusItems,System.Threading.CancellationToken)"/> on each of the returned folders.</para>
		/// </remarks>
		/// <returns>The subfolders.</returns>
		/// <param name="items">The status items to pre-populate.</param>
		/// <param name="subscribedOnly">If set to <see langword="true" />, only subscribed folders will be listed.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<IList<IMailFolder>> GetSubfoldersAsync (StatusItems items, bool subscribedOnly = false, CancellationToken cancellationToken = default);

		/// <summary>
		/// Get the subfolders.
		/// </summary>
		/// <remarks>
		/// Gets the subfolders.
		/// </remarks>
		/// <returns>The subfolders.</returns>
		/// <param name="subscribedOnly">If set to <see langword="true" />, only subscribed folders will be listed.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual IList<IMailFolder> GetSubfolders (bool subscribedOnly = false, CancellationToken cancellationToken = default)
		{
			return GetSubfolders (StatusItems.None, subscribedOnly, cancellationToken);
		}

		/// <summary>
		/// Asynchronously get the subfolders.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets the subfolders.
		/// </remarks>
		/// <returns>The subfolders.</returns>
		/// <param name="subscribedOnly">If set to <see langword="true" />, only subscribed folders will be listed.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task<IList<IMailFolder>> GetSubfoldersAsync (bool subscribedOnly = false, CancellationToken cancellationToken = default)
		{
			return GetSubfoldersAsync (StatusItems.None, subscribedOnly, cancellationToken);
		}

		/// <summary>
		/// Get the specified subfolder.
		/// </summary>
		/// <remarks>
		/// Gets the specified subfolder.
		/// </remarks>
		/// <returns>The subfolder.</returns>
		/// <param name="name">The name of the subfolder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="name"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="name"/> is either an empty string or contains the <see cref="DirectorySeparator"/>.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The requested folder could not be found.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract IMailFolder GetSubfolder (string name, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously get the specified subfolder.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets the specified subfolder.
		/// </remarks>
		/// <returns>The subfolder.</returns>
		/// <param name="name">The name of the subfolder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="name"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="name"/> is either an empty string or contains the <see cref="DirectorySeparator"/>.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The requested folder could not be found.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<IMailFolder> GetSubfolderAsync (string name, CancellationToken cancellationToken = default);

		/// <summary>
		/// Force the server to flush its state for the folder.
		/// </summary>
		/// <remarks>
		/// Forces the server to flush its state for the folder.
		/// </remarks>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void Check (CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously force the server to flush its state for the folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously forces the server to flush its state for the folder.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task CheckAsync (CancellationToken cancellationToken = default);

		/// <summary>
		/// Update the values of the specified items.
		/// </summary>
		/// <remarks>
		/// <para>Updates the values of the specified items.</para>
		/// <para>The <see cref="Status(StatusItems, System.Threading.CancellationToken)"/> method
		/// MUST NOT be used on a folder that is already in the opened state. Instead, other ways
		/// of getting the desired information should be used.</para>
		/// <para>For example, a common use for the <see cref="Status(StatusItems,System.Threading.CancellationToken)"/>
		/// method is to get the number of unread messages in the folder. When the folder is open, however, it is
		/// possible to use the <see cref="MailFolder.Search(MailKit.Search.SearchQuery, System.Threading.CancellationToken)"/>
		/// method to query for the list of unread messages.</para>
		/// </remarks>
		/// <param name="items">The items to update.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the STATUS command.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void Status (StatusItems items, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously update the values of the specified items.
		/// </summary>
		/// <remarks>
		/// <para>Updates the values of the specified items.</para>
		/// <para>The <see cref="Status(StatusItems, System.Threading.CancellationToken)"/> method
		/// MUST NOT be used on a folder that is already in the opened state. Instead, other ways
		/// of getting the desired information should be used.</para>
		/// <para>For example, a common use for the <see cref="Status(StatusItems,System.Threading.CancellationToken)"/>
		/// method is to get the number of unread messages in the folder. When the folder is open, however, it is
		/// possible to use the <see cref="MailFolder.Search(MailKit.Search.SearchQuery, System.Threading.CancellationToken)"/>
		/// method to query for the list of unread messages.</para>
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="items">The items to update.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the STATUS command.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task StatusAsync (StatusItems items, CancellationToken cancellationToken = default);

		/// <summary>
		/// Get the complete access control list for the folder.
		/// </summary>
		/// <remarks>
		/// Gets the complete access control list for the folder.
		/// </remarks>
		/// <returns>The access control list.</returns>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the ACL extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract AccessControlList GetAccessControlList (CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously get the complete access control list for the folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets the complete access control list for the folder.
		/// </remarks>
		/// <returns>The access control list.</returns>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the ACL extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<AccessControlList> GetAccessControlListAsync (CancellationToken cancellationToken = default);

		/// <summary>
		/// Get the access rights for a particular identifier.
		/// </summary>
		/// <remarks>
		/// Gets the access rights for a particular identifier.
		/// </remarks>
		/// <returns>The access rights.</returns>
		/// <param name="name">The identifier name.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="name"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the ACL extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract AccessRights GetAccessRights (string name, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously get the access rights for a particular identifier.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets the access rights for a particular identifier.
		/// </remarks>
		/// <returns>The access rights.</returns>
		/// <param name="name">The identifier name.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="name"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the ACL extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<AccessRights> GetAccessRightsAsync (string name, CancellationToken cancellationToken = default);

		/// <summary>
		/// Get the access rights for the current authenticated user.
		/// </summary>
		/// <remarks>
		/// Gets the access rights for the current authenticated user.
		/// </remarks>
		/// <returns>The access rights.</returns>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the ACL extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract AccessRights GetMyAccessRights (CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously get the access rights for the current authenticated user.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets the access rights for the current authenticated user.
		/// </remarks>
		/// <returns>The access rights.</returns>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the ACL extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<AccessRights> GetMyAccessRightsAsync (CancellationToken cancellationToken = default);

		/// <summary>
		/// Add access rights for the specified identity.
		/// </summary>
		/// <remarks>
		/// Adds the given access rights for the specified identity.
		/// </remarks>
		/// <param name="name">The identity name.</param>
		/// <param name="rights">The access rights.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="name"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="rights"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the ACL extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void AddAccessRights (string name, AccessRights rights, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously add access rights for the specified identity.
		/// </summary>
		/// <remarks>
		/// Asynchronously adds the given access rights for the specified identity.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="name">The identity name.</param>
		/// <param name="rights">The access rights.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="name"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="rights"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the ACL extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task AddAccessRightsAsync (string name, AccessRights rights, CancellationToken cancellationToken = default);

		/// <summary>
		/// Remove access rights for the specified identity.
		/// </summary>
		/// <remarks>
		/// Removes the given access rights for the specified identity.
		/// </remarks>
		/// <param name="name">The identity name.</param>
		/// <param name="rights">The access rights.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="name"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="rights"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the ACL extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void RemoveAccessRights (string name, AccessRights rights, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously remove access rights for the specified identity.
		/// </summary>
		/// <remarks>
		/// Asynchronously removes the given access rights for the specified identity.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="name">The identity name.</param>
		/// <param name="rights">The access rights.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="name"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="rights"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the ACL extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task RemoveAccessRightsAsync (string name, AccessRights rights, CancellationToken cancellationToken = default);

		/// <summary>
		/// Set the access rights for the specified identity.
		/// </summary>
		/// <remarks>
		/// Sets the access rights for the specified identity.
		/// </remarks>
		/// <param name="name">The identity name.</param>
		/// <param name="rights">The access rights.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="name"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="rights"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the ACL extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void SetAccessRights (string name, AccessRights rights, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously set the access rights for the specified identity.
		/// </summary>
		/// <remarks>
		/// Asynchronously sets the access rights for the specified identity.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="name">The identity name.</param>
		/// <param name="rights">The access rights.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="name"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="rights"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the ACL extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task SetAccessRightsAsync (string name, AccessRights rights, CancellationToken cancellationToken = default);

		/// <summary>
		/// Remove all access rights for the given identity.
		/// </summary>
		/// <remarks>
		/// Removes all access rights for the given identity.
		/// </remarks>
		/// <param name="name">The identity name.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="name"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the ACL extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void RemoveAccess (string name, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously remove all access rights for the given identity.
		/// </summary>
		/// <remarks>
		/// Asynchronously removes all access rights for the given identity.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="name">The identity name.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="name"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the ACL extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task RemoveAccessAsync (string name, CancellationToken cancellationToken = default);

		/// <summary>
		/// Get the quota information for the folder.
		/// </summary>
		/// <remarks>
		/// <para>Gets the quota information for the folder.</para>
		/// <para>To determine if a quotas are supported, check the 
		/// <see cref="IMailStore.SupportsQuotas"/> property.</para>
		/// </remarks>
		/// <returns>The folder quota.</returns>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support quotas.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract FolderQuota GetQuota (CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously get the quota information for the folder.
		/// </summary>
		/// <remarks>
		/// <para>Asynchronously gets the quota information for the folder.</para>
		/// <para>To determine if a quotas are supported, check the 
		/// <see cref="IMailStore.SupportsQuotas"/> property.</para>
		/// </remarks>
		/// <returns>The folder quota.</returns>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support quotas.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<FolderQuota> GetQuotaAsync (CancellationToken cancellationToken = default);

		/// <summary>
		/// Set the quota limits for the folder.
		/// </summary>
		/// <remarks>
		/// <para>Sets the quota limits for the folder.</para>
		/// <para>To determine if a quotas are supported, check the 
		/// <see cref="IMailStore.SupportsQuotas"/> property.</para>
		/// </remarks>
		/// <returns>The updated folder quota.</returns>
		/// <param name="messageLimit">If not <see langword="null" />, sets the maximum number of messages to allow.</param>
		/// <param name="storageLimit">If not <see langword="null" />, sets the maximum storage size (in kilobytes).</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support quotas.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract FolderQuota SetQuota (uint? messageLimit, uint? storageLimit, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously set the quota limits for the folder.
		/// </summary>
		/// <remarks>
		/// <para>Asynchronously sets the quota limits for the folder.</para>
		/// <para>To determine if a quotas are supported, check the 
		/// <see cref="IMailStore.SupportsQuotas"/> property.</para>
		/// </remarks>
		/// <returns>The updated folder quota.</returns>
		/// <param name="messageLimit">If not <see langword="null" />, sets the maximum number of messages to allow.</param>
		/// <param name="storageLimit">If not <see langword="null" />, sets the maximum storage size (in kilobytes).</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support quotas.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<FolderQuota> SetQuotaAsync (uint? messageLimit, uint? storageLimit, CancellationToken cancellationToken = default);

		/// <summary>
		/// Get the specified metadata.
		/// </summary>
		/// <remarks>
		/// Gets the specified metadata.
		/// </remarks>
		/// <returns>The requested metadata value.</returns>
		/// <param name="tag">The metadata tag.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The folder does not support metadata.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract string GetMetadata (MetadataTag tag, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously gets the specified metadata.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets the specified metadata.
		/// </remarks>
		/// <returns>The requested metadata value.</returns>
		/// <param name="tag">The metadata tag.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The folder does not support metadata.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<string> GetMetadataAsync (MetadataTag tag, CancellationToken cancellationToken = default);

		/// <summary>
		/// Get the specified metadata.
		/// </summary>
		/// <remarks>
		/// Gets the specified metadata.
		/// </remarks>
		/// <returns>The requested metadata.</returns>
		/// <param name="tags">The metadata tags.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="tags"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The folder does not support metadata.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public MetadataCollection GetMetadata (IEnumerable<MetadataTag> tags, CancellationToken cancellationToken = default)
		{
			return GetMetadata (new MetadataOptions (), tags, cancellationToken);
		}

		/// <summary>
		/// Asynchronously gets the specified metadata.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets the specified metadata.
		/// </remarks>
		/// <returns>The requested metadata.</returns>
		/// <param name="tags">The metadata tags.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="tags"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The folder does not support metadata.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task<MetadataCollection> GetMetadataAsync (IEnumerable<MetadataTag> tags, CancellationToken cancellationToken = default)
		{
			return GetMetadataAsync (new MetadataOptions (), tags, cancellationToken);
		}

		/// <summary>
		/// Get the specified metadata.
		/// </summary>
		/// <remarks>
		/// Gets the specified metadata.
		/// </remarks>
		/// <returns>The requested metadata.</returns>
		/// <param name="options">The metadata options.</param>
		/// <param name="tags">The metadata tags.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="options"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="tags"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The folder does not support metadata.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract MetadataCollection GetMetadata (MetadataOptions options, IEnumerable<MetadataTag> tags, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously gets the specified metadata.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets the specified metadata.
		/// </remarks>
		/// <returns>The requested metadata.</returns>
		/// <param name="options">The metadata options.</param>
		/// <param name="tags">The metadata tags.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="options"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="tags"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The folder does not support metadata.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<MetadataCollection> GetMetadataAsync (MetadataOptions options, IEnumerable<MetadataTag> tags, CancellationToken cancellationToken = default);

		/// <summary>
		/// Sets the specified metadata.
		/// </summary>
		/// <remarks>
		/// Sets the specified metadata.
		/// </remarks>
		/// <param name="metadata">The metadata.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="metadata"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The folder does not support metadata.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void SetMetadata (MetadataCollection metadata, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously sets the specified metadata.
		/// </summary>
		/// <remarks>
		/// Asynchronously sets the specified metadata.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="metadata">The metadata.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="metadata"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The folder does not support metadata.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task SetMetadataAsync (MetadataCollection metadata, CancellationToken cancellationToken = default);

		/// <summary>
		/// Expunge the folder, permanently removing all messages marked for deletion.
		/// </summary>
		/// <remarks>
		/// <para>Expunges the folder, permanently removing all messages marked for deletion.</para>
		/// <note type="note">Normally, an <see cref="MessageExpunged"/> event will be emitted for
		/// each message that is expunged. However, if the mail store supports the quick
		/// resynchronization feature and it has been enabled via the
		/// <see cref="MailStore.EnableQuickResync(CancellationToken)"/> method, then
		/// the <see cref="MessagesVanished"/> event will be emitted rather than the
		/// <see cref="MessageExpunged"/> event.</note>
		/// </remarks>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void Expunge (CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously expunge the folder, permanently removing all messages marked for deletion.
		/// </summary>
		/// <remarks>
		/// <para>Asynchronously expunges the folder, permanently removing all messages marked for deletion.</para>
		/// <note type="note">Normally, an <see cref="MessageExpunged"/> event will be emitted for
		/// each message that is expunged. However, if the mail store supports the quick
		/// resynchronization feature and it has been enabled via the
		/// <see cref="MailStore.EnableQuickResync(CancellationToken)"/> method, then
		/// the <see cref="MessagesVanished"/> event will be emitted rather than the
		/// <see cref="MessageExpunged"/> event.</note>
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task ExpungeAsync (CancellationToken cancellationToken = default);

		/// <summary>
		/// Expunge the specified uids, permanently removing them from the folder.
		/// </summary>
		/// <remarks>
		/// <para>Expunges the specified uids, permanently removing them from the folder.</para>
		/// <note type="note">Normally, an <see cref="MessageExpunged"/> event will be emitted for
		/// each message that is expunged. However, if the mail store supports the quick
		/// resynchronization feature and it has been enabled via the
		/// <see cref="MailStore.EnableQuickResync(CancellationToken)"/> method, then
		/// the <see cref="MessagesVanished"/> event will be emitted rather than the
		/// <see cref="MessageExpunged"/> event.</note>
		/// </remarks>
		/// <param name="uids">The message uids.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="uids"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="uids"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void Expunge (IList<UniqueId> uids, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously expunge the specified uids, permanently removing them from the folder.
		/// </summary>
		/// <remarks>
		/// <para>Asynchronously expunges the specified uids, permanently removing them from the folder.</para>
		/// <note type="note">Normally, an <see cref="MessageExpunged"/> event will be emitted for
		/// each message that is expunged. However, if the mail store supports the quick
		/// resynchronization feature and it has been enabled via the
		/// <see cref="MailStore.EnableQuickResync(CancellationToken)"/> method, then
		/// the <see cref="MessagesVanished"/> event will be emitted rather than the
		/// <see cref="MessageExpunged"/> event.</note>
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="uids">The message uids.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="uids"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="uids"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task ExpungeAsync (IList<UniqueId> uids, CancellationToken cancellationToken = default);

		/// <summary>
		/// Append a message to the folder.
		/// </summary>
		/// <remarks>
		/// Appends a message to the folder and returns the UniqueId assigned to the message.
		/// </remarks>
		/// <returns>The UID of the appended message, if available; otherwise, <see langword="null" />.</returns>
		/// <param name="request">The append request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="request"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Internationalized formatting was requested but has not been enabled.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>Internationalized formatting was requested but is not supported by the server.</para>
		/// <para>-or-</para>
		/// <para>The request included annotations but the folder does not support annotations.</para>
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual UniqueId? Append (IAppendRequest request, CancellationToken cancellationToken = default)
		{
			return Append (FormatOptions.Default, request, cancellationToken);
		}

		/// <summary>
		/// Asynchronously append a message to the folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously appends a message to the folder and returns the UniqueId assigned to the message.
		/// </remarks>
		/// <returns>The UID of the appended message, if available; otherwise, <see langword="null" />.</returns>
		/// <param name="request">The append request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="request"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Internationalized formatting was requested but has not been enabled.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>Internationalized formatting was requested but is not supported by the server.</para>
		/// <para>-or-</para>
		/// <para>The request included annotations but the folder does not support annotations.</para>
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task<UniqueId?> AppendAsync (IAppendRequest request, CancellationToken cancellationToken = default)
		{
			return AppendAsync (FormatOptions.Default, request, cancellationToken);
		}

		/// <summary>
		/// Append a message to the folder.
		/// </summary>
		/// <remarks>
		/// Appends a message to the folder and returns the UniqueId assigned to the message.
		/// </remarks>
		/// <returns>The UID of the appended message, if available; otherwise, <see langword="null" />.</returns>
		/// <param name="options">The formatting options.</param>
		/// <param name="request">The append request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="options"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Internationalized formatting was requested but has not been enabled.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>Internationalized formatting was requested but is not supported by the server.</para>
		/// <para>-or-</para>
		/// <para>The request included annotations but the folder does not support annotations.</para>
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract UniqueId? Append (FormatOptions options, IAppendRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously append a message to the folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously appends a message to the folder and returns the UniqueId assigned to the message.
		/// </remarks>
		/// <returns>The UID of the appended message, if available; otherwise, <see langword="null" />.</returns>
		/// <param name="options">The formatting options.</param>
		/// <param name="request">The append request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="options"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Internationalized formatting was requested but has not been enabled.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>Internationalized formatting was requested but is not supported by the server.</para>
		/// <para>-or-</para>
		/// <para>The request included annotations but the folder does not support annotations.</para>
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<UniqueId?> AppendAsync (FormatOptions options, IAppendRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Append multiple messages to the folder.
		/// </summary>
		/// <remarks>
		/// Appends multiple messages to the folder and returns the UniqueIds assigned to the messages.
		/// </remarks>
		/// <returns>The UIDs of the appended messages, if available; otherwise an empty array.</returns>
		/// <param name="requests">The append requests.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="requests"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="ArgumentException">
		/// One or more of the <paramref name="requests"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Internationalized formatting was requested but has not been enabled.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>Internationalized formatting was requested but is not supported by the server.</para>
		/// <para>-or-</para>
		/// <para>One ore more requests included annotations but the folder does not support annotations.</para>
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual IList<UniqueId> Append (IList<IAppendRequest> requests, CancellationToken cancellationToken = default)
		{
			return Append (FormatOptions.Default, requests, cancellationToken);
		}

		/// <summary>
		/// Asynchronously append multiple messages to the folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously appends multiple messages to the folder and returns the UniqueIds assigned to the messages.
		/// </remarks>
		/// <returns>The UIDs of the appended messages, if available; otherwise an empty array.</returns>
		/// <param name="requests">The append requests.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="requests"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="ArgumentException">
		/// One or more of the <paramref name="requests"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Internationalized formatting was requested but has not been enabled.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>Internationalized formatting was requested but is not supported by the server.</para>
		/// <para>-or-</para>
		/// <para>One ore more requests included annotations but the folder does not support annotations.</para>
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task<IList<UniqueId>> AppendAsync (IList<IAppendRequest> requests, CancellationToken cancellationToken = default)
		{
			return AppendAsync (FormatOptions.Default, requests, cancellationToken);
		}

		/// <summary>
		/// Append multiple messages to the folder.
		/// </summary>
		/// <remarks>
		/// Appends multiple messages to the folder and returns the UniqueIds assigned to the messages.
		/// </remarks>
		/// <returns>The UIDs of the appended messages, if available; otherwise an empty array.</returns>
		/// <param name="options">The formatting options.</param>
		/// <param name="requests">The append requests.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="options"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="requests"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="ArgumentException">
		/// One or more of the <paramref name="requests"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Internationalized formatting was requested but has not been enabled.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>Internationalized formatting was requested but is not supported by the server.</para>
		/// <para>-or-</para>
		/// <para>One ore more requests included annotations but the folder does not support annotations.</para>
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract IList<UniqueId> Append (FormatOptions options, IList<IAppendRequest> requests, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously append multiple messages to the folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously appends multiple messages to the folder and returns the UniqueIds assigned to the messages.
		/// </remarks>
		/// <returns>The UIDs of the appended messages, if available; otherwise an empty array.</returns>
		/// <param name="options">The formatting options.</param>
		/// <param name="requests">The append requests.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="options"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="requests"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="ArgumentException">
		/// One or more of the <paramref name="requests"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Internationalized formatting was requested but has not been enabled.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>Internationalized formatting was requested but is not supported by the server.</para>
		/// <para>-or-</para>
		/// <para>One ore more requests included annotations but the folder does not support annotations.</para>
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<IList<UniqueId>> AppendAsync (FormatOptions options, IList<IAppendRequest> requests, CancellationToken cancellationToken = default);

		/// <summary>
		/// Replace a message in the folder.
		/// </summary>
		/// <remarks>
		/// Replaces a message in the folder and returns the UniqueId assigned to the new message.
		/// </remarks>
		/// <returns>The UID of the new message, if available; otherwise, <see langword="null" />.</returns>
		/// <param name="uid">The UID of the message to be replaced.</param>
		/// <param name="request">The replace request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uid"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para>The destination folder does not belong to this <see cref="IMailStore"/>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Internationalized formatting was requested but has not been enabled.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="MailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The server replied with a NO or BAD response.
		/// </exception>
		public virtual UniqueId? Replace (UniqueId uid, IReplaceRequest request, CancellationToken cancellationToken = default)
		{
			return Replace (FormatOptions.Default, uid, request, cancellationToken);
		}

		/// <summary>
		/// Asynchronously replace a message in the folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously replaces a message in the folder and returns the UniqueId assigned to the new message.
		/// </remarks>
		/// <returns>The UID of the appended message, if available; otherwise, <see langword="null" />.</returns>
		/// <param name="uid">The UID of the message to be replaced.</param>
		/// <param name="request">The replace request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uid"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para>The destination folder does not belong to this <see cref="IMailStore"/>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Internationalized formatting was requested but has not been enabled.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="MailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The server replied with a NO or BAD response.
		/// </exception>
		public virtual Task<UniqueId?> ReplaceAsync (UniqueId uid, IReplaceRequest request, CancellationToken cancellationToken = default)
		{
			return ReplaceAsync (FormatOptions.Default, uid, request, cancellationToken);
		}

		/// <summary>
		/// Replace a message in the folder.
		/// </summary>
		/// <remarks>
		/// Replaces a message in the folder and returns the UniqueId assigned to the new message.
		/// </remarks>
		/// <returns>The UID of the new message, if available; otherwise, <see langword="null" />.</returns>
		/// <param name="options">The formatting options.</param>
		/// <param name="uid">The UID of the message to be replaced.</param>
		/// <param name="request">The replace request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="options"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uid"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para>The destination folder does not belong to this <see cref="IMailStore"/>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Internationalized formatting was requested but has not been enabled.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="MailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>Internationalized formatting was requested but is not supported by the server.</para>
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The server replied with a NO or BAD response.
		/// </exception>
		public abstract UniqueId? Replace (FormatOptions options, UniqueId uid, IReplaceRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously replace a message in the folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously replaces a message in the folder and returns the UniqueId assigned to the new message.
		/// </remarks>
		/// <returns>The UID of the new message, if available; otherwise, <see langword="null" />.</returns>
		/// <param name="options">The formatting options.</param>
		/// <param name="uid">The UID of the message to be replaced.</param>
		/// <param name="request">The replace request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="options"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uid"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para>The destination folder does not belong to this <see cref="IMailStore"/>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Internationalized formatting was requested but has not been enabled.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="MailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>Internationalized formatting was requested but is not supported by the server.</para>
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The server replied with a NO or BAD response.
		/// </exception>
		public abstract Task<UniqueId?> ReplaceAsync (FormatOptions options, UniqueId uid, IReplaceRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Replace a message in the folder.
		/// </summary>
		/// <remarks>
		/// Replaces a message in the folder and returns the UniqueId assigned to the new message.
		/// </remarks>
		/// <returns>The UID of the new message, if available; otherwise, <see langword="null" />.</returns>
		/// <param name="index">The index of the message to be replaced.</param>
		/// <param name="request">The replace request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> is out of range.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The destination folder does not belong to this <see cref="IMailStore"/>.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Internationalized formatting was requested but has not been enabled.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="MailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The server replied with a NO or BAD response.
		/// </exception>
		public virtual UniqueId? Replace (int index, IReplaceRequest request, CancellationToken cancellationToken = default)
		{
			return Replace (FormatOptions.Default, index, request, cancellationToken);
		}

		/// <summary>
		/// Asynchronously replace a message in the folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously replaces a message in the folder and returns the UniqueId assigned to the new message.
		/// </remarks>
		/// <returns>The UID of the appended message, if available; otherwise, <see langword="null" />.</returns>
		/// <param name="index">The index of the message to be replaced.</param>
		/// <param name="request">The replace request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> is out of range.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The destination folder does not belong to this <see cref="IMailStore"/>.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Internationalized formatting was requested but has not been enabled.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="MailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The server replied with a NO or BAD response.
		/// </exception>
		public virtual Task<UniqueId?> ReplaceAsync (int index, IReplaceRequest request, CancellationToken cancellationToken = default)
		{
			return ReplaceAsync (FormatOptions.Default, index, request, cancellationToken);
		}

		/// <summary>
		/// Replace a message in the folder.
		/// </summary>
		/// <remarks>
		/// Replaces a message in the folder and returns the UniqueId assigned to the new message.
		/// </remarks>
		/// <returns>The UID of the new message, if available; otherwise, <see langword="null" />.</returns>
		/// <param name="options">The formatting options.</param>
		/// <param name="index">The index of the message to be replaced.</param>
		/// <param name="request">The replace request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="options"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> is out of range.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The destination folder does not belong to this <see cref="IMailStore"/>.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Internationalized formatting was requested but has not been enabled.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="MailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>Internationalized formatting was requested but is not supported by the server.</para>
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The server replied with a NO or BAD response.
		/// </exception>
		public abstract UniqueId? Replace (FormatOptions options, int index, IReplaceRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously replace a message in the folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously replaces a message in the folder and returns the UniqueId assigned to the new message.
		/// </remarks>
		/// <returns>The UID of the new message, if available; otherwise, <see langword="null" />.</returns>
		/// <param name="options">The formatting options.</param>
		/// <param name="index">The index of the message to be replaced.</param>
		/// <param name="request">The replace request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="options"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> is out of range.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The destination folder does not belong to this <see cref="IMailStore"/>.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Internationalized formatting was requested but has not been enabled.
		/// </exception>
		/// <exception cref="FolderNotFoundException">
		/// The <see cref="MailFolder"/> does not exist.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="MailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>Internationalized formatting was requested but is not supported by the server.</para>
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The server replied with a NO or BAD response.
		/// </exception>
		public abstract Task<UniqueId?> ReplaceAsync (FormatOptions options, int index, IReplaceRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Copy the specified message to the destination folder.
		/// </summary>
		/// <remarks>
		/// Copies the specified message to the destination folder.
		/// </remarks>
		/// <returns>The UID of the message in the destination folder, if available; otherwise, <see langword="null" />.</returns>
		/// <param name="uid">The UID of the message to copy.</param>
		/// <param name="destination">The destination folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="destination"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uid"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para>The destination folder does not belong to the <see cref="IMailStore"/>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the UIDPLUS extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual UniqueId? CopyTo (UniqueId uid, IMailFolder destination, CancellationToken cancellationToken = default)
		{
			var uids = CopyTo (new [] { uid }, destination, cancellationToken);

			if (uids != null && uids.Destination.Count > 0)
				return uids.Destination[0];

			return null;
		}

		/// <summary>
		/// Asynchronously copy the specified message to the destination folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously copies the specified message to the destination folder.
		/// </remarks>
		/// <returns>The UID of the message in the destination folder, if available; otherwise, <see langword="null" />.</returns>
		/// <param name="uid">The UID of the message to copy.</param>
		/// <param name="destination">The destination folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="destination"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uid"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para>The destination folder does not belong to the <see cref="IMailStore"/>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the UIDPLUS extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual async Task<UniqueId?> CopyToAsync (UniqueId uid, IMailFolder destination, CancellationToken cancellationToken = default)
		{
			var uids = await CopyToAsync (new [] { uid }, destination, cancellationToken).ConfigureAwait (false);

			if (uids != null && uids.Destination.Count > 0)
				return uids.Destination[0];

			return null;
		}

		/// <summary>
		/// Copy the specified messages to the destination folder.
		/// </summary>
		/// <remarks>
		/// Copies the specified messages to the destination folder.
		/// </remarks>
		/// <returns>The UID mapping of the messages in the destination folder, if available; otherwise an empty mapping.</returns>
		/// <param name="uids">The UIDs of the messages to copy.</param>
		/// <param name="destination">The destination folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="destination"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para>One or more of the <paramref name="uids"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para>The destination folder does not belong to the <see cref="IMailStore"/>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the UIDPLUS extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract UniqueIdMap CopyTo (IList<UniqueId> uids, IMailFolder destination, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously copy the specified messages to the destination folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously copies the specified messages to the destination folder.
		/// </remarks>
		/// <returns>The UID mapping of the messages in the destination folder, if available; otherwise an empty mapping.</returns>
		/// <param name="uids">The UIDs of the messages to copy.</param>
		/// <param name="destination">The destination folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="destination"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para>One or more of the <paramref name="uids"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para>The destination folder does not belong to the <see cref="IMailStore"/>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the UIDPLUS extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<UniqueIdMap> CopyToAsync (IList<UniqueId> uids, IMailFolder destination, CancellationToken cancellationToken = default);

		/// <summary>
		/// Move the specified message to the destination folder.
		/// </summary>
		/// <remarks>
		/// Moves the specified message to the destination folder.
		/// </remarks>
		/// <returns>The UID of the message in the destination folder, if available; otherwise, <see langword="null" />.</returns>
		/// <param name="uid">The UID of the message to move.</param>
		/// <param name="destination">The destination folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="destination"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uid"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para>The destination folder does not belong to the <see cref="IMailStore"/>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the UIDPLUS extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual UniqueId? MoveTo (UniqueId uid, IMailFolder destination, CancellationToken cancellationToken = default)
		{
			var uids = MoveTo (new [] { uid }, destination, cancellationToken);

			if (uids != null && uids.Destination.Count > 0)
				return uids.Destination[0];

			return null;
		}

		/// <summary>
		/// Asynchronously move the specified message to the destination folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously moves the specified message to the destination folder.
		/// </remarks>
		/// <returns>The UID of the message in the destination folder, if available; otherwise, <see langword="null" />.</returns>
		/// <param name="uid">The UID of the message to move.</param>
		/// <param name="destination">The destination folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="destination"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uid"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para>The destination folder does not belong to the <see cref="IMailStore"/>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the UIDPLUS extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual async Task<UniqueId?> MoveToAsync (UniqueId uid, IMailFolder destination, CancellationToken cancellationToken = default)
		{
			var uids = await MoveToAsync (new [] { uid }, destination, cancellationToken).ConfigureAwait (false);

			if (uids != null && uids.Destination.Count > 0)
				return uids.Destination[0];

			return null;
		}

		/// <summary>
		/// Move the specified messages to the destination folder.
		/// </summary>
		/// <remarks>
		/// Moves the specified messages to the destination folder.
		/// </remarks>
		/// <returns>The UID mapping of the messages in the destination folder, if available; otherwise an empty mapping.</returns>
		/// <param name="uids">The UIDs of the messages to move.</param>
		/// <param name="destination">The destination folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="destination"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para>One or more of the <paramref name="uids"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para>The destination folder does not belong to the <see cref="IMailStore"/>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the UIDPLUS extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract UniqueIdMap MoveTo (IList<UniqueId> uids, IMailFolder destination, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously move the specified messages to the destination folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously moves the specified messages to the destination folder.
		/// </remarks>
		/// <returns>The UID mapping of the messages in the destination folder, if available; otherwise an empty mapping.</returns>
		/// <param name="uids">The UIDs of the messages to move.</param>
		/// <param name="destination">The destination folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="destination"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para>One or more of the <paramref name="uids"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para>The destination folder does not belong to the <see cref="IMailStore"/>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The mail store does not support the UIDPLUS extension.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<UniqueIdMap> MoveToAsync (IList<UniqueId> uids, IMailFolder destination, CancellationToken cancellationToken = default);

		/// <summary>
		/// Copy the specified message to the destination folder.
		/// </summary>
		/// <remarks>
		/// Copies the specified message to the destination folder.
		/// </remarks>
		/// <param name="index">The index of the message to copy.</param>
		/// <param name="destination">The destination folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="destination"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> does not refer to a valid message index.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The destination folder does not belong to the <see cref="IMailStore"/>.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual void CopyTo (int index, IMailFolder destination, CancellationToken cancellationToken = default)
		{
			if (index < 0 || index >= Count)
				throw new ArgumentOutOfRangeException (nameof (index));

			CopyTo (new [] { index }, destination, cancellationToken);
		}

		/// <summary>
		/// Asynchronously copy the specified message to the destination folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously copies the specified message to the destination folder.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="index">The indexes of the message to copy.</param>
		/// <param name="destination">The destination folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="destination"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> does not refer to a valid message index.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The destination folder does not belong to the <see cref="IMailStore"/>.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task CopyToAsync (int index, IMailFolder destination, CancellationToken cancellationToken = default)
		{
			if (index < 0 || index >= Count)
				throw new ArgumentOutOfRangeException (nameof (index));

			return CopyToAsync (new [] { index }, destination, cancellationToken);
		}

		/// <summary>
		/// Copy the specified messages to the destination folder.
		/// </summary>
		/// <remarks>
		/// Copies the specified messages to the destination folder.
		/// </remarks>
		/// <param name="indexes">The indexes of the messages to copy.</param>
		/// <param name="destination">The destination folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="indexes"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="destination"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para>One or more of the <paramref name="indexes"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para>The destination folder does not belong to the <see cref="IMailStore"/>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void CopyTo (IList<int> indexes, IMailFolder destination, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously copy the specified messages to the destination folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously copies the specified messages to the destination folder.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="indexes">The indexes of the messages to copy.</param>
		/// <param name="destination">The destination folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="indexes"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="destination"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para>One or more of the <paramref name="indexes"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para>The destination folder does not belong to the <see cref="IMailStore"/>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task CopyToAsync (IList<int> indexes, IMailFolder destination, CancellationToken cancellationToken = default);

		/// <summary>
		/// Move the specified message to the destination folder.
		/// </summary>
		/// <remarks>
		/// Moves the specified message to the destination folder.
		/// </remarks>
		/// <param name="index">The index of the message to move.</param>
		/// <param name="destination">The destination folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="destination"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> does not refer to a valid message index.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The destination folder does not belong to the <see cref="IMailStore"/>.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual void MoveTo (int index, IMailFolder destination, CancellationToken cancellationToken = default)
		{
			if (index < 0 || index >= Count)
				throw new ArgumentOutOfRangeException (nameof (index));

			MoveTo (new [] { index }, destination, cancellationToken);
		}

		/// <summary>
		/// Asynchronously move the specified message to the destination folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously moves the specified message to the destination folder.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="index">The index of the message to move.</param>
		/// <param name="destination">The destination folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="destination"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> does not refer to a valid message index.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// The destination folder does not belong to the <see cref="IMailStore"/>.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task MoveToAsync (int index, IMailFolder destination, CancellationToken cancellationToken = default)
		{
			if (index < 0 || index >= Count)
				throw new ArgumentOutOfRangeException (nameof (index));

			return MoveToAsync (new [] { index }, destination, cancellationToken);
		}

		/// <summary>
		/// Move the specified messages to the destination folder.
		/// </summary>
		/// <remarks>
		/// Moves the specified messages to the destination folder.
		/// </remarks>
		/// <param name="indexes">The indexes of the messages to move.</param>
		/// <param name="destination">The destination folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="indexes"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="destination"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para>One or more of the <paramref name="indexes"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para>The destination folder does not belong to the <see cref="IMailStore"/>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void MoveTo (IList<int> indexes, IMailFolder destination, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously move the specified messages to the destination folder.
		/// </summary>
		/// <remarks>
		/// Asynchronously moves the specified messages to the destination folder.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="indexes">The indexes of the messages to move.</param>
		/// <param name="destination">The destination folder.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="indexes"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="destination"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para>One or more of the <paramref name="indexes"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para>The destination folder does not belong to the <see cref="IMailStore"/>.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task MoveToAsync (IList<int> indexes, IMailFolder destination, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetch the message summaries for the specified message UIDs.
		/// </summary>
		/// <remarks>
		/// <para>Fetches the message summaries for the specified message UIDs.</para>
		/// <para>It should be noted that if another client has modified any message
		/// in the folder, the mail service may choose to return information that was
		/// not explicitly requested. It is therefore important to be prepared to
		/// handle both additional fields on a <see cref="IMessageSummary"/> for
		/// messages that were requested as well as summaries for messages that were
		/// not requested at all.</para>
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapBodyPartExamples.cs" region="GetBodyPartsByUniqueId"/>
		/// </example>
		/// <returns>An enumeration of summaries for the requested messages.</returns>
		/// <param name="uids">The UIDs.</param>
		/// <param name="request">The fetch request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="uids"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract IList<IMessageSummary> Fetch (IList<UniqueId> uids, IFetchRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously fetch the message summaries for the specified message UIDs.
		/// </summary>
		/// <remarks>
		/// <para>Asynchronously fetches the message summaries for the specified message
		/// UIDs.</para>
		/// <para>It should be noted that if another client has modified any message
		/// in the folder, the mail service may choose to return information that was
		/// not explicitly requested. It is therefore important to be prepared to
		/// handle both additional fields on a <see cref="IMessageSummary"/> for
		/// messages that were requested as well as summaries for messages that were
		/// not requested at all.</para>
		/// </remarks>
		/// <returns>An enumeration of summaries for the requested messages.</returns>
		/// <param name="uids">The UIDs.</param>
		/// <param name="request">The fetch request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="uids"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<IList<IMessageSummary>> FetchAsync (IList<UniqueId> uids, IFetchRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetch the message summaries for the specified message indexes.
		/// </summary>
		/// <remarks>
		/// <para>Fetches the message summaries for the specified message indexes.</para>
		/// <para>It should be noted that if another client has modified any message
		/// in the folder, the mail service may choose to return information that was
		/// not explicitly requested. It is therefore important to be prepared to
		/// handle both additional fields on a <see cref="IMessageSummary"/> for
		/// messages that were requested as well as summaries for messages that were
		/// not requested at all.</para>
		/// </remarks>
		/// <returns>An enumeration of summaries for the requested messages.</returns>
		/// <param name="indexes">The indexes.</param>
		/// <param name="request">The fetch request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="indexes"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="indexes"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract IList<IMessageSummary> Fetch (IList<int> indexes, IFetchRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously fetch the message summaries for the specified message indexes.
		/// </summary>
		/// <remarks>
		/// <para>Asynchronously fetches the message summaries for the specified message
		/// indexes.</para>
		/// <para>It should be noted that if another client has modified any message
		/// in the folder, the mail service may choose to return information that was
		/// not explicitly requested. It is therefore important to be prepared to
		/// handle both additional fields on a <see cref="IMessageSummary"/> for
		/// messages that were requested as well as summaries for messages that were
		/// not requested at all.</para>
		/// </remarks>
		/// <returns>An enumeration of summaries for the requested messages.</returns>
		/// <param name="indexes">The indexes.</param>
		/// <param name="request">The fetch request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="indexes"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="indexes"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<IList<IMessageSummary>> FetchAsync (IList<int> indexes, IFetchRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Fetch the message summaries for the messages between the two indexes, inclusive.
		/// </summary>
		/// <remarks>
		/// <para>Fetches the message summaries for the messages between the two
		/// indexes, inclusive.</para>
		/// <para>It should be noted that if another client has modified any message
		/// in the folder, the mail service may choose to return information that was
		/// not explicitly requested. It is therefore important to be prepared to
		/// handle both additional fields on a <see cref="IMessageSummary"/> for
		/// messages that were requested as well as summaries for messages that were
		/// not requested at all.</para>
		/// </remarks>
		/// <returns>An enumeration of summaries for the requested messages.</returns>
		/// <param name="min">The minimum index.</param>
		/// <param name="max">The maximum index, or <c>-1</c> to specify no upper bound.</param>
		/// <param name="request">The fetch request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="request"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="min"/> is out of range.</para>
		/// <para>-or-</para>
		/// <para><paramref name="max"/> is out of range.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract IList<IMessageSummary> Fetch (int min, int max, IFetchRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously fetch the message summaries for the messages between the two indexes, inclusive.
		/// </summary>
		/// <remarks>
		/// <para>Asynchronously fetches the message summaries for the messages between
		/// the two indexes, inclusive.</para>
		/// <para>It should be noted that if another client has modified any message
		/// in the folder, the mail service may choose to return information that was
		/// not explicitly requested. It is therefore important to be prepared to
		/// handle both additional fields on a <see cref="IMessageSummary"/> for
		/// messages that were requested as well as summaries for messages that were
		/// not requested at all.</para>
		/// </remarks>
		/// <returns>An enumeration of summaries for the requested messages.</returns>
		/// <param name="min">The minimum index.</param>
		/// <param name="max">The maximum index, or <c>-1</c> to specify no upper bound.</param>
		/// <param name="request">The fetch request.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="request"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="min"/> is out of range.</para>
		/// <para>-or-</para>
		/// <para><paramref name="max"/> is out of range.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<IList<IMessageSummary>> FetchAsync (int min, int max, IFetchRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Get the specified message headers.
		/// </summary>
		/// <remarks>
		/// Gets the specified message headers.
		/// </remarks>
		/// <returns>The message headers.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message headers.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract HeaderList GetHeaders (UniqueId uid, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Asynchronously get the specified message headers.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets the specified message headers.
		/// </remarks>
		/// <returns>The message headers.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message headers.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<HeaderList> GetHeadersAsync (UniqueId uid, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Get the specified body part headers.
		/// </summary>
		/// <remarks>
		/// Gets the specified body part headers.
		/// </remarks>
		/// <returns>The body part headers.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="part">The body part.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="part"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested body part headers.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract HeaderList GetHeaders (UniqueId uid, BodyPart part, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Asynchronously get the specified body part headers.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets the specified body part headers.
		/// </remarks>
		/// <returns>The body part headers.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="part">The body part.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="part"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested body part headers.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<HeaderList> GetHeadersAsync (UniqueId uid, BodyPart part, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Get the specified message headers.
		/// </summary>
		/// <remarks>
		/// Gets the specified message headers.
		/// </remarks>
		/// <returns>The message headers.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> is out of range.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message headers.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract HeaderList GetHeaders (int index, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Asynchronously get the specified message headers.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets the specified message headers.
		/// </remarks>
		/// <returns>The message headers.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> is out of range.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message headers.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<HeaderList> GetHeadersAsync (int index, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Get the specified body part headers.
		/// </summary>
		/// <remarks>
		/// Gets the specified body part headers.
		/// </remarks>
		/// <returns>The body part headers.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="part">The body part.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> is out of range.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="part"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested body part headers.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract HeaderList GetHeaders (int index, BodyPart part, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Asynchronously get the specified body part headers.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets the specified body part headers.
		/// </remarks>
		/// <returns>The body part headers.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="part">The body part.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> is out of range.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="part"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested body part headers.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<HeaderList> GetHeadersAsync (int index, BodyPart part, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Get the specified message.
		/// </summary>
		/// <remarks>
		/// Gets the specified message.
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapExamples.cs" region="DownloadMessagesByUniqueId"/>
		/// </example>
		/// <returns>The message.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract MimeMessage GetMessage (UniqueId uid, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Asynchronously get the specified message.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets the specified message.
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapExamples.cs" region="DownloadMessagesByUniqueId"/>
		/// </example>
		/// <returns>The message.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<MimeMessage> GetMessageAsync (UniqueId uid, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Get the specified message.
		/// </summary>
		/// <remarks>
		/// Gets the specified message.
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapExamples.cs" region="DownloadMessagesByIndex"/>
		/// </example>
		/// <returns>The message.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> is out of range.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract MimeMessage GetMessage (int index, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Asynchronously get the specified message.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets the specified message.
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapExamples.cs" region="DownloadMessagesByIndex"/>
		/// </example>
		/// <returns>The message.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> is out of range.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<MimeMessage> GetMessageAsync (int index, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Get the specified body part.
		/// </summary>
		/// <remarks>
		/// Gets the specified body part.
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapBodyPartExamples.cs" region="GetBodyPartsByUniqueId"/>
		/// </example>
		/// <returns>The body part.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="part">The body part.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="part"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message body.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract MimeEntity GetBodyPart (UniqueId uid, BodyPart part, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Asynchronously get the specified body part.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets the specified body part.
		/// </remarks>
		/// <returns>The body part.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="part">The body part.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="part"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message body.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<MimeEntity> GetBodyPartAsync (UniqueId uid, BodyPart part, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Get the specified body part.
		/// </summary>
		/// <remarks>
		/// Gets the specified body part.
		/// </remarks>
		/// <returns>The body part.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="part">The body part.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="part"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> is out of range.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message body.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract MimeEntity GetBodyPart (int index, BodyPart part, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Asynchronously get the specified body part.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets the specified body part.
		/// </remarks>
		/// <returns>The body part.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="part">The body part.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="part"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> is out of range.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message body.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<MimeEntity> GetBodyPartAsync (int index, BodyPart part, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Get a message stream.
		/// </summary>
		/// <remarks>
		/// Gets a message stream.
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapExamples.cs" region="DownloadMessageStreamsByUniqueId"/>
		/// </example>
		/// <returns>The message stream.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Stream GetStream (UniqueId uid, CancellationToken cancellationToken = default, ITransferProgress progress = null)
		{
			return GetStream (uid, string.Empty, cancellationToken, progress);
		}

		/// <summary>
		/// Asynchronously get a message stream.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets a message stream.
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapExamples.cs" region="DownloadMessageStreamsByUniqueId"/>
		/// </example>
		/// <returns>The message stream.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task<Stream> GetStreamAsync (UniqueId uid, CancellationToken cancellationToken = default, ITransferProgress progress = null)
		{
			return GetStreamAsync (uid, string.Empty, cancellationToken, progress);
		}

		/// <summary>
		/// Get a message stream.
		/// </summary>
		/// <remarks>
		/// Gets a message stream.
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapExamples.cs" region="DownloadMessageStreamsByIndex"/>
		/// </example>
		/// <returns>The message stream.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="index"/> is out of range.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Stream GetStream (int index, CancellationToken cancellationToken = default, ITransferProgress progress = null)
		{
			return GetStream (index, string.Empty, cancellationToken, progress);
		}

		/// <summary>
		/// Asynchronously get a message stream.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets a message stream.
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapExamples.cs" region="DownloadMessageStreamsByIndex"/>
		/// </example>
		/// <returns>The message stream.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="index"/> is out of range.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task<Stream> GetStreamAsync (int index, CancellationToken cancellationToken = default, ITransferProgress progress = null)
		{
			return GetStreamAsync (index, string.Empty, cancellationToken, progress);
		}

		/// <summary>
		/// Get a substream of the specified message.
		/// </summary>
		/// <remarks>
		/// Gets a substream of the message. If the starting offset is beyond
		/// the end of the message, an empty stream is returned. If the number of
		/// bytes desired extends beyond the end of the message, a truncated stream
		/// will be returned.
		/// </remarks>
		/// <returns>The stream.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="offset">The starting offset of the first desired byte.</param>
		/// <param name="count">The number of bytes desired.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="offset"/> is negative.</para>
		/// <para>-or-</para>
		/// <para><paramref name="count"/> is negative.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Stream GetStream (UniqueId uid, int offset, int count, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Asynchronously get a substream of the specified message.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets a substream of the message. If the starting offset is beyond
		/// the end of the message, an empty stream is returned. If the number of
		/// bytes desired extends beyond the end of the message, a truncated stream
		/// will be returned.
		/// </remarks>
		/// <returns>The stream.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="offset">The starting offset of the first desired byte.</param>
		/// <param name="count">The number of bytes desired.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="offset"/> is negative.</para>
		/// <para>-or-</para>
		/// <para><paramref name="count"/> is negative.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<Stream> GetStreamAsync (UniqueId uid, int offset, int count, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Get a substream of the specified message.
		/// </summary>
		/// <remarks>
		/// Gets a substream of the message. If the starting offset is beyond
		/// the end of the message, an empty stream is returned. If the number of
		/// bytes desired extends beyond the end of the message, a truncated stream
		/// will be returned.
		/// </remarks>
		/// <returns>The stream.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="offset">The starting offset of the first desired byte.</param>
		/// <param name="count">The number of bytes desired.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="index"/> is out of range.</para>
		/// <para>-or-</para>
		/// <para><paramref name="offset"/> is negative.</para>
		/// <para>-or-</para>
		/// <para><paramref name="count"/> is negative.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Stream GetStream (int index, int offset, int count, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Asynchronously get a substream of the specified message.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets a substream of the message. If the starting offset is beyond
		/// the end of the message, an empty stream is returned. If the number of
		/// bytes desired extends beyond the end of the message, a truncated stream
		/// will be returned.
		/// </remarks>
		/// <returns>The stream.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="offset">The starting offset of the first desired byte.</param>
		/// <param name="count">The number of bytes desired.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="index"/> is out of range.</para>
		/// <para>-or-</para>
		/// <para><paramref name="offset"/> is negative.</para>
		/// <para>-or-</para>
		/// <para><paramref name="count"/> is negative.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<Stream> GetStreamAsync (int index, int offset, int count, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Get a body part as a stream.
		/// </summary>
		/// <remarks>
		/// Gets a body part as a stream.
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapBodyPartExamples.cs" region="GetBodyPartStreamsByUniqueId"/>
		/// </example>
		/// <returns>The body part stream.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="part">The desired body part.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="part"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Stream GetStream (UniqueId uid, BodyPart part, CancellationToken cancellationToken = default, ITransferProgress progress = null)
		{
			if (!uid.IsValid)
				throw new ArgumentException ("The uid is invalid.", nameof (uid));

			if (part == null)
				throw new ArgumentNullException (nameof (part));

			return GetStream (uid, part.PartSpecifier, cancellationToken, progress);
		}

		/// <summary>
		/// Asynchronously get a body part as a stream.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets a body part as a stream.
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapBodyPartExamples.cs" region="GetBodyPartStreamsByUniqueId"/>
		/// </example>
		/// <returns>The body part stream.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="part">The desired body part.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="part"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task<Stream> GetStreamAsync (UniqueId uid, BodyPart part, CancellationToken cancellationToken = default, ITransferProgress progress = null)
		{
			if (!uid.IsValid)
				throw new ArgumentException ("The uid is invalid.", nameof (uid));

			if (part == null)
				throw new ArgumentNullException (nameof (part));

			return GetStreamAsync (uid, part.PartSpecifier, cancellationToken, progress);
		}

		/// <summary>
		/// Get a body part as a stream.
		/// </summary>
		/// <remarks>
		/// Gets a body part as a stream.
		/// </remarks>
		/// <returns>The body part stream.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="part">The desired body part.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="part"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="index"/> is out of range.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Stream GetStream (int index, BodyPart part, CancellationToken cancellationToken = default, ITransferProgress progress = null)
		{
			if (index < 0 || index >= Count)
				throw new ArgumentOutOfRangeException (nameof (index));

			if (part == null)
				throw new ArgumentNullException (nameof (part));

			return GetStream (index, part.PartSpecifier, cancellationToken, progress);
		}

		/// <summary>
		/// Asynchronously get a body part as a stream.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets a body part as a stream.
		/// </remarks>
		/// <returns>The body part stream.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="part">The desired body part.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="part"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="index"/> is out of range.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task<Stream> GetStreamAsync (int index, BodyPart part, CancellationToken cancellationToken = default, ITransferProgress progress = null)
		{
			if (index < 0 || index >= Count)
				throw new ArgumentOutOfRangeException (nameof (index));

			if (part == null)
				throw new ArgumentNullException (nameof (part));

			return GetStreamAsync (index, part.PartSpecifier, cancellationToken, progress);
		}

		/// <summary>
		/// Get a substream of the specified body part.
		/// </summary>
		/// <remarks>
		/// Gets a substream of the body part. If the starting offset is beyond
		/// the end of the body part, an empty stream is returned. If the number of
		/// bytes desired extends beyond the end of the body part, a truncated stream
		/// will be returned.
		/// </remarks>
		/// <returns>The stream.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="part">The desired body part.</param>
		/// <param name="offset">The starting offset of the first desired byte.</param>
		/// <param name="count">The number of bytes desired.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="part"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="offset"/> is negative.</para>
		/// <para>-or-</para>
		/// <para><paramref name="count"/> is negative.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Stream GetStream (UniqueId uid, BodyPart part, int offset, int count, CancellationToken cancellationToken = default, ITransferProgress progress = null)
		{
			if (!uid.IsValid)
				throw new ArgumentException ("The uid is invalid.", nameof (uid));

			if (part == null)
				throw new ArgumentNullException (nameof (part));

			if (offset < 0)
				throw new ArgumentOutOfRangeException (nameof (offset));

			if (count < 0)
				throw new ArgumentOutOfRangeException (nameof (count));

			return GetStream (uid, part.PartSpecifier, offset, count, cancellationToken, progress);
		}

		/// <summary>
		/// Asynchronously get a substream of the specified body part.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets a substream of the body part. If the starting offset is beyond
		/// the end of the body part, an empty stream is returned. If the number of
		/// bytes desired extends beyond the end of the body part, a truncated stream
		/// will be returned.
		/// </remarks>
		/// <returns>The stream.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="part">The desired body part.</param>
		/// <param name="offset">The starting offset of the first desired byte.</param>
		/// <param name="count">The number of bytes desired.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="part"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="offset"/> is negative.</para>
		/// <para>-or-</para>
		/// <para><paramref name="count"/> is negative.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task<Stream> GetStreamAsync (UniqueId uid, BodyPart part, int offset, int count, CancellationToken cancellationToken = default, ITransferProgress progress = null)
		{
			if (!uid.IsValid)
				throw new ArgumentException ("The uid is invalid.", nameof (uid));

			if (part == null)
				throw new ArgumentNullException (nameof (part));

			if (offset < 0)
				throw new ArgumentOutOfRangeException (nameof (offset));

			if (count < 0)
				throw new ArgumentOutOfRangeException (nameof (count));

			return GetStreamAsync (uid, part.PartSpecifier, offset, count, cancellationToken, progress);
		}

		/// <summary>
		/// Get a substream of the specified body part.
		/// </summary>
		/// <remarks>
		/// Gets a substream of the body part. If the starting offset is beyond
		/// the end of the body part, an empty stream is returned. If the number of
		/// bytes desired extends beyond the end of the body part, a truncated stream
		/// will be returned.
		/// </remarks>
		/// <returns>The stream.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="part">The desired body part.</param>
		/// <param name="offset">The starting offset of the first desired byte.</param>
		/// <param name="count">The number of bytes desired.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="part"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="index"/> is out of range.</para>
		/// <para>-or-</para>
		/// <para><paramref name="offset"/> is negative.</para>
		/// <para>-or-</para>
		/// <para><paramref name="count"/> is negative.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Stream GetStream (int index, BodyPart part, int offset, int count, CancellationToken cancellationToken = default, ITransferProgress progress = null)
		{
			if (index < 0 || index >= Count)
				throw new ArgumentOutOfRangeException (nameof (index));

			if (part == null)
				throw new ArgumentNullException (nameof (part));

			if (offset < 0)
				throw new ArgumentOutOfRangeException (nameof (offset));

			if (count < 0)
				throw new ArgumentOutOfRangeException (nameof (count));

			return GetStream (index, part.PartSpecifier, offset, count, cancellationToken, progress);
		}

		/// <summary>
		/// Asynchronously get a substream of the specified body part.
		/// </summary>
		/// <remarks>
		/// Asynchronously gets a substream of the body part. If the starting offset is beyond
		/// the end of the body part, an empty stream is returned. If the number of
		/// bytes desired extends beyond the end of the body part, a truncated stream
		/// will be returned.
		/// </remarks>
		/// <returns>The stream.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="part">The desired body part.</param>
		/// <param name="offset">The starting offset of the first desired byte.</param>
		/// <param name="count">The number of bytes desired.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="part"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="index"/> is out of range.</para>
		/// <para>-or-</para>
		/// <para><paramref name="offset"/> is negative.</para>
		/// <para>-or-</para>
		/// <para><paramref name="count"/> is negative.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task<Stream> GetStreamAsync (int index, BodyPart part, int offset, int count, CancellationToken cancellationToken = default, ITransferProgress progress = null)
		{
			if (index < 0 || index >= Count)
				throw new ArgumentOutOfRangeException (nameof (index));

			if (part == null)
				throw new ArgumentNullException (nameof (part));

			if (offset < 0)
				throw new ArgumentOutOfRangeException (nameof (offset));

			if (count < 0)
				throw new ArgumentOutOfRangeException (nameof (count));

			return GetStreamAsync (index, part.PartSpecifier, offset, count, cancellationToken, progress);
		}

		/// <summary>
		/// Get a substream of the specified message.
		/// </summary>
		/// <remarks>
		/// <para>Gets a substream of the specified message.</para>
		/// <para>For more information about how to construct the <paramref name="section"/>,
		/// see Section 6.4.5 of RFC3501.</para>
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapBodyPartExamples.cs" region="GetBodyPartStreamsByUniqueIdAndSpecifier"/>
		/// </example>
		/// <returns>The stream.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="section">The desired section of the message.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="section"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Stream GetStream (UniqueId uid, string section, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Asynchronously get a substream of the specified message.
		/// </summary>
		/// <remarks>
		/// <para>Asynchronously gets a substream of the specified message.</para>
		/// <para>For more information about how to construct the <paramref name="section"/>,
		/// see Section 6.4.5 of RFC3501.</para>
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapBodyPartExamples.cs" region="GetBodyPartStreamsByUniqueIdAndSpecifier"/>
		/// </example>
		/// <returns>The stream.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="section">The desired section of the message.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="section"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<Stream> GetStreamAsync (UniqueId uid, string section, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Get a substream of the specified message.
		/// </summary>
		/// <remarks>
		/// <para>Gets a substream of the specified message. If the starting offset is beyond
		/// the end of the specified section of the message, an empty stream is returned. If
		/// the number of bytes desired extends beyond the end of the section, a truncated
		/// stream will be returned.</para>
		/// <para>For more information about how to construct the <paramref name="section"/>,
		/// see Section 6.4.5 of RFC3501.</para>
		/// </remarks>
		/// <returns>The stream.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="section">The desired section of the message.</param>
		/// <param name="offset">The starting offset of the first desired byte.</param>
		/// <param name="count">The number of bytes desired.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="section"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="offset"/> is negative.</para>
		/// <para>-or-</para>
		/// <para><paramref name="count"/> is negative.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Stream GetStream (UniqueId uid, string section, int offset, int count, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Asynchronously get a substream of the specified message.
		/// </summary>
		/// <remarks>
		/// <para>Asynchronously gets a substream of the specified message. If the starting
		/// offset is beyond the end of the specified section of the message, an empty stream
		/// is returned. If the number of bytes desired extends beyond the end of the section,
		/// a truncated stream will be returned.</para>
		/// <para>For more information about how to construct the <paramref name="section"/>,
		/// see Section 6.4.5 of RFC3501.</para>
		/// </remarks>
		/// <returns>The stream.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="section">The desired section of the message.</param>
		/// <param name="offset">The starting offset of the first desired byte.</param>
		/// <param name="count">The number of bytes desired.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="section"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="offset"/> is negative.</para>
		/// <para>-or-</para>
		/// <para><paramref name="count"/> is negative.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<Stream> GetStreamAsync (UniqueId uid, string section, int offset, int count, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Get a substream of the specified message.
		/// </summary>
		/// <remarks>
		/// <para>Gets a substream of the specified message.</para>
		/// <para>For more information about how to construct the <paramref name="section"/>,
		/// see Section 6.4.5 of RFC3501.</para>
		/// </remarks>
		/// <returns>The stream.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="section">The desired section of the message.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="section"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> is out of range.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Stream GetStream (int index, string section, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Asynchronously get a substream of the specified body part.
		/// </summary>
		/// <remarks>
		/// <para>Asynchronously gets a substream of the specified message.</para>
		/// <para>For more information about how to construct the <paramref name="section"/>,
		/// see Section 6.4.5 of RFC3501.</para>
		/// </remarks>
		/// <returns>The stream.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="section">The desired section of the message.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="section"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="index"/> is out of range.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<Stream> GetStreamAsync (int index, string section, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Get a substream of the specified message.
		/// </summary>
		/// <remarks>
		/// <para>Gets a substream of the specified message. If the starting offset is beyond
		/// the end of the specified section of the message, an empty stream is returned. If
		/// the number of bytes desired extends beyond the end of the section, a truncated
		/// stream will be returned.</para>
		/// <para>For more information about how to construct the <paramref name="section"/>,
		/// see Section 6.4.5 of RFC3501.</para>
		/// </remarks>
		/// <returns>The stream.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="section">The desired section of the message.</param>
		/// <param name="offset">The starting offset of the first desired byte.</param>
		/// <param name="count">The number of bytes desired.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="section"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="index"/> is out of range.</para>
		/// <para>-or-</para>
		/// <para><paramref name="offset"/> is negative.</para>
		/// <para>-or-</para>
		/// <para><paramref name="count"/> is negative.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Stream GetStream (int index, string section, int offset, int count, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Asynchronously get a substream of the specified body part.
		/// </summary>
		/// <remarks>
		/// <para>Asynchronously gets a substream of the specified message. If the starting
		/// offset is beyond the end of the specified section of the message, an empty stream
		/// is returned. If the number of bytes desired extends beyond the end of the section,
		/// a truncated stream will be returned.</para>
		/// <para>For more information about how to construct the <paramref name="section"/>,
		/// see Section 6.4.5 of RFC3501.</para>
		/// </remarks>
		/// <returns>The stream.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="section">The desired section of the message.</param>
		/// <param name="offset">The starting offset of the first desired byte.</param>
		/// <param name="count">The number of bytes desired.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <param name="progress">The progress reporting mechanism.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="section"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <para><paramref name="index"/> is out of range.</para>
		/// <para>-or-</para>
		/// <para><paramref name="offset"/> is negative.</para>
		/// <para>-or-</para>
		/// <para><paramref name="count"/> is negative.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="MessageNotFoundException">
		/// The <see cref="IMailStore"/> did not return the requested message stream.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<Stream> GetStreamAsync (int index, string section, int offset, int count, CancellationToken cancellationToken = default, ITransferProgress progress = null);

		/// <summary>
		/// Store message flags and keywords for a message.
		/// </summary>
		/// <remarks>
		/// Updates the message flags and keywords for a message.
		/// </remarks>
		/// <returns><see langword="true" /> if the store operation was successful; otherwise, <see langword="false" />.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="request">The message flags and keywords to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="request"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <paramref name="request"/> specified an <see cref="IStoreRequest.UnchangedSince"/>
		/// value but the folder does not support mod-sequences.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual bool Store (UniqueId uid, IStoreFlagsRequest request, CancellationToken cancellationToken = default)
		{
			var uids = Store (new[] { uid }, request, cancellationToken);

			return uids == null || uids.Count == 0;
		}

		/// <summary>
		/// Asynchronously store message flags and keywords for a message.
		/// </summary>
		/// <remarks>
		/// Asynchronously updates the message flags and keywords for a message.
		/// </remarks>
		/// <returns><see langword="true" /> if the store operation was successful; otherwise, <see langword="false" />.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="request">The message flags and keywords to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="request"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <paramref name="request"/> specified an <see cref="IStoreRequest.UnchangedSince"/>
		/// value but the folder does not support mod-sequences.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual async Task<bool> StoreAsync (UniqueId uid, IStoreFlagsRequest request, CancellationToken cancellationToken = default)
		{
			var uids = await StoreAsync (new[] { uid }, request, cancellationToken).ConfigureAwait (false);
			
			return uids == null || uids.Count == 0;
		}

		/// <summary>
		/// Store message flags and keywords for a set of messages.
		/// </summary>
		/// <remarks>
		/// Updates the message flags and keywords for a set of messages.
		/// </remarks>
		/// <returns>The UIDs of the messages that were not updated.</returns>
		/// <param name="uids">The message UIDs.</param>
		/// <param name="request">The message flags and keywords to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="uids"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <paramref name="request"/> specified an <see cref="IStoreRequest.UnchangedSince"/>
		/// value but the folder does not support mod-sequences.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract IList<UniqueId> Store (IList<UniqueId> uids, IStoreFlagsRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously store message flags and keywords for a set of messages.
		/// </summary>
		/// <remarks>
		/// Asynchronously updates the message flags and keywords for a set of messages.
		/// </remarks>
		/// <returns>The UIDs of the messages that were not updated.</returns>
		/// <param name="uids">The message UIDs.</param>
		/// <param name="request">The message flags and keywords to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="uids"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <paramref name="request"/> specified an <see cref="IStoreRequest.UnchangedSince"/>
		/// value but the folder does not support mod-sequences.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<IList<UniqueId>> StoreAsync (IList<UniqueId> uids, IStoreFlagsRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Store message flags and keywords for a message.
		/// </summary>
		/// <remarks>
		/// Updates the message flags and keywords for a message.
		/// </remarks>
		/// <returns><see langword="true" /> if the store operation was successful; otherwise, <see langword="false" />.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="request">The message flags and keywords to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="index"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="request"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <paramref name="request"/> specified an <see cref="IStoreRequest.UnchangedSince"/>
		/// value but the folder does not support mod-sequences.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual bool Store (int index, IStoreFlagsRequest request, CancellationToken cancellationToken = default)
		{
			var indexes = Store (new[] { index }, request, cancellationToken);

			return indexes == null || indexes.Count == 0;
		}

		/// <summary>
		/// Asynchronously store message flags and keywords for a message.
		/// </summary>
		/// <remarks>
		/// Asynchronously updates the message flags and keywords for a message.
		/// </remarks>
		/// <returns><see langword="true" /> if the store operation was successful; otherwise, <see langword="false" />.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="request">The message flags and keywords to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="index"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="request"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <paramref name="request"/> specified an <see cref="IStoreRequest.UnchangedSince"/>
		/// value but the folder does not support mod-sequences.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual async Task<bool> StoreAsync (int index, IStoreFlagsRequest request, CancellationToken cancellationToken = default)
		{
			var indexes = await StoreAsync (new[] { index }, request, cancellationToken).ConfigureAwait (false);

			return indexes == null || indexes.Count == 0;
		}

		/// <summary>
		/// Store message flags and keywords for a set of messages.
		/// </summary>
		/// <remarks>
		/// Updates the message flags and keywords for a set of messages.
		/// </remarks>
		/// <returns>The indexes of the messages that were not updated.</returns>
		/// <param name="indexes">The message indexes.</param>
		/// <param name="request">The message flags and keywords to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="indexes"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="indexes"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <paramref name="request"/> specified an <see cref="IStoreRequest.UnchangedSince"/>
		/// value but the folder does not support mod-sequences.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract IList<int> Store (IList<int> indexes, IStoreFlagsRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously store message flags and keywords for a set of messages.
		/// </summary>
		/// <remarks>
		/// Asynchronously updates the message flags and keywords for a set of messages.
		/// </remarks>
		/// <returns>The indexes of the messages that were not updated.</returns>
		/// <param name="indexes">The message indexes.</param>
		/// <param name="request">The message flags and keywords to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="indexes"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="indexes"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <paramref name="request"/> specified an <see cref="IStoreRequest.UnchangedSince"/>
		/// value but the folder does not support mod-sequences.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<IList<int>> StoreAsync (IList<int> indexes, IStoreFlagsRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Store GMail-style labels for a message.
		/// </summary>
		/// <remarks>
		/// Updates the GMail-style labels for a message.
		/// </remarks>
		/// <returns><see langword="true" /> if the store operation was successful; otherwise, <see langword="false" />.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="request">The GMail-style labels to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="request"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>The folder does not support storing labels.</para>
		/// <para>-or-</para>
		/// <para>The <paramref name="request"/> specified an <see cref="IStoreRequest.UnchangedSince"/>
		/// value but the folder does not support mod-sequences.</para>
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual bool Store (UniqueId uid, IStoreLabelsRequest request, CancellationToken cancellationToken = default)
		{
			var uids = Store (new[] { uid }, request, cancellationToken);

			return uids == null || uids.Count == 0;
		}

		/// <summary>
		/// Asynchronously store GMail-style labels for a message.
		/// </summary>
		/// <remarks>
		/// Asynchronously updates the GMail-style labels for a message.
		/// </remarks>
		/// <returns><see langword="true" /> if the store operation was successful; otherwise, <see langword="false" />.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="request">The GMail-style labels to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="request"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>The folder does not support storing labels.</para>
		/// <para>-or-</para>
		/// <para>The <paramref name="request"/> specified an <see cref="IStoreRequest.UnchangedSince"/>
		/// value but the folder does not support mod-sequences.</para>
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual async Task<bool> StoreAsync (UniqueId uid, IStoreLabelsRequest request, CancellationToken cancellationToken = default)
		{
			var uids = await StoreAsync (new[] { uid }, request, cancellationToken).ConfigureAwait (false);

			return uids == null || uids.Count == 0;
		}

		/// <summary>
		/// Store GMail-style labels for a set of messages.
		/// </summary>
		/// <remarks>
		/// Updates the GMail-style labels for a set of messages.
		/// </remarks>
		/// <returns>The UIDs of the messages that were not updated.</returns>
		/// <param name="uids">The message UIDs.</param>
		/// <param name="request">The GMail-style labels to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="uids"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>The folder does not support storing labels.</para>
		/// <para>-or-</para>
		/// <para>The <paramref name="request"/> specified an <see cref="IStoreRequest.UnchangedSince"/>
		/// value but the folder does not support mod-sequences.</para>
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract IList<UniqueId> Store (IList<UniqueId> uids, IStoreLabelsRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously store GMail-style labels for a set of messages.
		/// </summary>
		/// <remarks>
		/// Asynchronously updates the GMail-style labels for a set of messages.
		/// </remarks>
		/// <returns>The UIDs of the messages that were not updated.</returns>
		/// <param name="uids">The message UIDs.</param>
		/// <param name="request">The GMail-style labels to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="uids"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>The folder does not support storing labels.</para>
		/// <para>-or-</para>
		/// <para>The <paramref name="request"/> specified an <see cref="IStoreRequest.UnchangedSince"/>
		/// value but the folder does not support mod-sequences.</para>
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<IList<UniqueId>> StoreAsync (IList<UniqueId> uids, IStoreLabelsRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Store GMail-style labels for a message.
		/// </summary>
		/// <remarks>
		/// Updates the GMail-style labels for a message.
		/// </remarks>
		/// <returns><see langword="true" /> if the store operation was successful; otherwise, <see langword="false" />.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="request">The GMail-style labels to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="index"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="request"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>The folder does not support storing labels.</para>
		/// <para>-or-</para>
		/// <para>The <paramref name="request"/> specified an <see cref="IStoreRequest.UnchangedSince"/>
		/// value but the folder does not support mod-sequences.</para>
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual bool Store (int index, IStoreLabelsRequest request, CancellationToken cancellationToken = default)
		{
			var indexes = Store (new[] { index }, request, cancellationToken);

			return indexes == null || indexes.Count == 0;
		}

		/// <summary>
		/// Asynchronously store GMail-style labels for a message.
		/// </summary>
		/// <remarks>
		/// Asynchronously updates the GMail-style labels for a message.
		/// </remarks>
		/// <returns><see langword="true" /> if the store operation was successful; otherwise, <see langword="false" />.</returns>
		/// <param name="index">The index of the message.</param>
		/// <param name="request">The GMail-style labels to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="index"/> is invalid.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="request"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>The folder does not support storing labels.</para>
		/// <para>-or-</para>
		/// <para>The <paramref name="request"/> specified an <see cref="IStoreRequest.UnchangedSince"/>
		/// value but the folder does not support mod-sequences.</para>
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual async Task<bool> StoreAsync (int index, IStoreLabelsRequest request, CancellationToken cancellationToken = default)
		{
			var indexes = await StoreAsync (new[] { index }, request, cancellationToken).ConfigureAwait (false);

			return indexes == null || indexes.Count == 0;
		}

		/// <summary>
		/// Store GMail-style labels for a set of messages.
		/// </summary>
		/// <remarks>
		/// Updates the GMail-style labels for a set of messages.
		/// </remarks>
		/// <returns>The indexes of the messages that were not updated.</returns>
		/// <param name="indexes">The message indexes.</param>
		/// <param name="request">The GMail-style labels to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="indexes"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="indexes"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>The folder does not support storing labels.</para>
		/// <para>-or-</para>
		/// <para>The <paramref name="request"/> specified an <see cref="IStoreRequest.UnchangedSince"/>
		/// value but the folder does not support mod-sequences.</para>
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract IList<int> Store (IList<int> indexes, IStoreLabelsRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously store GMail-style labels for a set of messages.
		/// </summary>
		/// <remarks>
		/// Asynchronously updates the GMail-style labels for a set of messages.
		/// </remarks>
		/// <returns>The indexes of the messages that were not updated.</returns>
		/// <param name="indexes">The message indexes.</param>
		/// <param name="request">The GMail-style labels to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="indexes"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="request"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="indexes"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>The folder does not support storing labels.</para>
		/// <para>-or-</para>
		/// <para>The <paramref name="request"/> specified an <see cref="IStoreRequest.UnchangedSince"/>
		/// value but the folder does not support mod-sequences.</para>
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<IList<int>> StoreAsync (IList<int> indexes, IStoreLabelsRequest request, CancellationToken cancellationToken = default);

		/// <summary>
		/// Store the annotations for the specified message.
		/// </summary>
		/// <remarks>
		/// Stores the annotations for the specified message.
		/// </remarks>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="annotations">The annotations to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="annotations"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="IMailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Cannot store annotations without any properties defined.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <see cref="IMailFolder"/> does not support annotations.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual void Store (UniqueId uid, IList<Annotation> annotations, CancellationToken cancellationToken = default)
		{
			Store (new [] { uid }, annotations, cancellationToken);
		}

		/// <summary>
		/// Asynchronously store the annotations for the specified message.
		/// </summary>
		/// <remarks>
		/// Asynchronously stores the annotations for the specified message.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="uid">The UID of the message.</param>
		/// <param name="annotations">The annotations to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="annotations"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="uid"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="IMailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Cannot store annotations without any properties defined.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <see cref="IMailFolder"/> does not support annotations.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task StoreAsync (UniqueId uid, IList<Annotation> annotations, CancellationToken cancellationToken = default)
		{
			return StoreAsync (new [] { uid }, annotations, cancellationToken);
		}

		/// <summary>
		/// Store the annotations for the specified messages.
		/// </summary>
		/// <remarks>
		/// Stores the annotations for the specified messages.
		/// </remarks>
		/// <param name="uids">The UIDs of the messages.</param>
		/// <param name="annotations">The annotations to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="annotations"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="uids"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="IMailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Cannot store annotations without any properties defined.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <see cref="IMailFolder"/> does not support annotations.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void Store (IList<UniqueId> uids, IList<Annotation> annotations, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously store the annotations for the specified messages.
		/// </summary>
		/// <remarks>
		/// Asynchronously stores the annotations for the specified messages.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="uids">The UIDs of the messages.</param>
		/// <param name="annotations">The annotations to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="annotations"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="uids"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="IMailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Cannot store annotations without any properties defined.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <see cref="IMailFolder"/> does not support annotations.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task StoreAsync (IList<UniqueId> uids, IList<Annotation> annotations, CancellationToken cancellationToken = default);

		/// <summary>
		/// Store the annotations for the specified messages only if their mod-sequence value is less than the specified value.
		/// </summary>
		/// <remarks>
		/// Stores the annotations for the specified messages only if their mod-sequence value is less than the specified value.
		/// </remarks>
		/// <returns>The unique IDs of the messages that were not updated.</returns>
		/// <param name="uids">The UIDs of the messages.</param>
		/// <param name="modseq">The mod-sequence value.</param>
		/// <param name="annotations">The annotations to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="annotations"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="uids"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="IMailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Cannot store annotations without any properties defined.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>The <see cref="IMailFolder"/> does not support annotations.</para>
		/// <para>-or-</para>
		/// <para>The <see cref="IMailFolder"/> does not support mod-sequences.</para>
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract IList<UniqueId> Store (IList<UniqueId> uids, ulong modseq, IList<Annotation> annotations, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously store the annotations for the specified messages only if their mod-sequence value is less than the specified value.
		/// </summary>
		/// <remarks>
		/// Asynchronously stores the annotations for the specified messages only if their mod-sequence value is less than the specified value.
		/// </remarks>
		/// <returns>The unique IDs of the messages that were not updated.</returns>
		/// <param name="uids">The UIDs of the messages.</param>
		/// <param name="modseq">The mod-sequence value.</param>
		/// <param name="annotations">The annotations to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="annotations"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="uids"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="IMailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Cannot store annotations without any properties defined.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>The <see cref="IMailFolder"/> does not support annotations.</para>
		/// <para>-or-</para>
		/// <para>The <see cref="IMailFolder"/> does not support mod-sequences.</para>
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<IList<UniqueId>> StoreAsync (IList<UniqueId> uids, ulong modseq, IList<Annotation> annotations, CancellationToken cancellationToken = default);

		/// <summary>
		/// Store the annotations for the specified message.
		/// </summary>
		/// <remarks>
		/// Stores the annotations for the specified message.
		/// </remarks>
		/// <param name="index">The index of the message.</param>
		/// <param name="annotations">The annotations to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="annotations"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="index"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="IMailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Cannot store annotations without any properties defined.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <see cref="IMailFolder"/> does not support annotations.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual void Store (int index, IList<Annotation> annotations, CancellationToken cancellationToken = default)
		{
			Store (new[] { index }, annotations, cancellationToken);
		}

		/// <summary>
		/// Asynchronously store the annotations for the specified message.
		/// </summary>
		/// <remarks>
		/// Asynchronously stores the annotations for the specified message.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="index">The indexes of the message.</param>
		/// <param name="annotations">The annotations to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="annotations"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="index"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="IMailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Cannot store annotations without any properties defined.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <see cref="IMailFolder"/> does not support annotations.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task StoreAsync (int index, IList<Annotation> annotations, CancellationToken cancellationToken = default)
		{
			return StoreAsync (new [] { index }, annotations, cancellationToken);
		}

		/// <summary>
		/// Store the annotations for the specified messages.
		/// </summary>
		/// <remarks>
		/// Stores the annotations for the specified messages.
		/// </remarks>
		/// <param name="indexes">The indexes of the messages.</param>
		/// <param name="annotations">The annotations to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="indexes"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="annotations"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="indexes"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="IMailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Cannot store annotations without any properties defined.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <see cref="IMailFolder"/> does not support annotations.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract void Store (IList<int> indexes, IList<Annotation> annotations, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously store the annotations for the specified messages.
		/// </summary>
		/// <remarks>
		/// Asynchronously stores the annotations for the specified messages.
		/// </remarks>
		/// <returns>An asynchronous task context.</returns>
		/// <param name="indexes">The indexes of the messages.</param>
		/// <param name="annotations">The annotations to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="indexes"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="annotations"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="indexes"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="IMailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Cannot store annotations without any properties defined.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// The <see cref="IMailFolder"/> does not support annotations.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task StoreAsync (IList<int> indexes, IList<Annotation> annotations, CancellationToken cancellationToken = default);

		/// <summary>
		/// Store the annotations for the specified messages only if their mod-sequence value is less than the specified value.
		/// </summary>
		/// <remarks>
		/// Stores the annotations for the specified messages only if their mod-sequence value is less than the specified value.
		/// </remarks>
		/// <returns>The indexes of the messages that were not updated.</returns>
		/// <param name="indexes">The indexes of the messages.</param>
		/// <param name="modseq">The mod-sequence value.</param>
		/// <param name="annotations">The annotations to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="indexes"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="annotations"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="indexes"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="IMailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Cannot store annotations without any properties defined.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>The <see cref="IMailFolder"/> does not support annotations.</para>
		/// <para>-or-</para>
		/// <para>The <see cref="IMailFolder"/> does not support mod-sequences.</para>
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract IList<int> Store (IList<int> indexes, ulong modseq, IList<Annotation> annotations, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously store the annotations for the specified messages only if their mod-sequence value is less than the specified value.
		/// </summary>
		/// <remarks>
		/// Asynchronously stores the annotations for the specified messages only if their mod-sequence value is less than the specified value.s
		/// </remarks>
		/// <returns>The indexes of the messages that were not updated.</returns>
		/// <param name="indexes">The indexes of the messages.</param>
		/// <param name="modseq">The mod-sequence value.</param>
		/// <param name="annotations">The annotations to store.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="indexes"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="annotations"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// One or more of the <paramref name="indexes"/> is invalid.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="IMailFolder"/> is not currently open in read-write mode.
		/// </exception>
		/// <exception cref="System.InvalidOperationException">
		/// Cannot store annotations without any properties defined.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>The <see cref="IMailFolder"/> does not support annotations.</para>
		/// <para>-or-</para>
		/// <para>The <see cref="IMailFolder"/> does not support mod-sequences.</para>
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<IList<int>> StoreAsync (IList<int> indexes, ulong modseq, IList<Annotation> annotations, CancellationToken cancellationToken = default);

		/// <summary>
		/// Search the folder for messages matching the specified query.
		/// </summary>
		/// <remarks>
		/// The returned array of unique identifiers can be used with methods such as
		/// <see cref="IMailFolder.GetMessage(UniqueId,CancellationToken,ITransferProgress)"/>.
		/// </remarks>
		/// <returns>An array of matching UIDs.</returns>
		/// <param name="query">The search query.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="query"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// One or more search terms in the <paramref name="query"/> are not supported by the mail store.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual IList<UniqueId> Search (SearchQuery query, CancellationToken cancellationToken = default)
		{
			var results = Search (SearchOptions.None, query, cancellationToken);

			return results.UniqueIds;
		}

		/// <summary>
		/// Asynchronously search the folder for messages matching the specified query.
		/// </summary>
		/// <remarks>
		/// The returned array of unique identifiers can be used with methods such as
		/// <see cref="IMailFolder.GetMessage(UniqueId,CancellationToken,ITransferProgress)"/>.
		/// </remarks>
		/// <returns>An array of matching UIDs.</returns>
		/// <param name="query">The search query.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="query"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// One or more search terms in the <paramref name="query"/> are not supported by the mail store.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual async Task<IList<UniqueId>> SearchAsync (SearchQuery query, CancellationToken cancellationToken = default)
		{
			var results = await SearchAsync (SearchOptions.None, query, cancellationToken).ConfigureAwait (false);

			return results.UniqueIds;
		}

		/// <summary>
		/// Search the subset of UIDs in the folder for messages matching the specified query.
		/// </summary>
		/// <remarks>
		/// The returned array of unique identifiers can be used with methods such as
		/// <see cref="IMailFolder.GetMessage(UniqueId,CancellationToken,ITransferProgress)"/>.
		/// </remarks>
		/// <returns>An array of matching UIDs in the specified sort order.</returns>
		/// <param name="uids">The subset of UIDs</param>
		/// <param name="query">The search query.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="query"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uids"/> is empty.</para>
		/// <para>-or-</para>
		/// <para>One or more of the <paramref name="uids"/> is invalid.</para>
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// One or more search terms in the <paramref name="query"/> are not supported by the mail store.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual IList<UniqueId> Search (IList<UniqueId> uids, SearchQuery query, CancellationToken cancellationToken = default)
		{
			var uidSet = new UidSearchQuery (uids);

			if (query == null)
				throw new ArgumentNullException (nameof (query));

			return Search (uidSet.And (query), cancellationToken);
		}

		/// <summary>
		/// Asynchronously search the subset of UIDs in the folder for messages matching the specified query.
		/// </summary>
		/// <remarks>
		/// The returned array of unique identifiers can be used with methods such as
		/// <see cref="IMailFolder.GetMessage(UniqueId,CancellationToken,ITransferProgress)"/>.
		/// </remarks>
		/// <returns>An array of matching UIDs in the specified sort order.</returns>
		/// <param name="uids">The subset of UIDs</param>
		/// <param name="query">The search query.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="query"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uids"/> is empty.</para>
		/// <para>-or-</para>
		/// <para>One or more of the <paramref name="uids"/> is invalid.</para>
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// One or more search terms in the <paramref name="query"/> are not supported.
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task<IList<UniqueId>> SearchAsync (IList<UniqueId> uids, SearchQuery query, CancellationToken cancellationToken = default)
		{
			var uidSet = new UidSearchQuery (uids);

			if (query == null)
				throw new ArgumentNullException (nameof (query));

			return SearchAsync (uidSet.And (query), cancellationToken);
		}

		/// <summary>
		/// Search the folder for messages matching the specified query.
		/// </summary>
		/// <remarks>
		/// Searches the folder for messages matching the specified query,
		/// returning only the specified search results.
		/// </remarks>
		/// <returns>The search results.</returns>
		/// <param name="options">The search options.</param>
		/// <param name="query">The search query.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="query"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>One or more search terms in the <paramref name="query"/> are not supported.</para>
		/// <para>-or-</para>
		/// <para>The server does not support the specified search options.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="MailFolder"/> is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract SearchResults Search (SearchOptions options, SearchQuery query, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously search the folder for messages matching the specified query.
		/// </summary>
		/// <remarks>
		/// Asynchronously searches the folder for messages matching the specified query,
		/// returning only the specified search results.
		/// </remarks>
		/// <returns>The search results.</returns>
		/// <param name="options">The search options.</param>
		/// <param name="query">The search query.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="query"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>One or more search terms in the <paramref name="query"/> are not supported.</para>
		/// <para>-or-</para>
		/// <para>The server does not support the specified search options.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="MailFolder"/> is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<SearchResults> SearchAsync (SearchOptions options, SearchQuery query, CancellationToken cancellationToken = default);

		/// <summary>
		/// Search the subset of UIDs in the folder for messages matching the specified query.
		/// </summary>
		/// <remarks>
		/// Searches the subset of UIDs in the folder for messages matching the specified query,
		/// returning only the specified search results.
		/// </remarks>
		/// <returns>The search results.</returns>
		/// <param name="options">The search options.</param>
		/// <param name="uids">The subset of UIDs</param>
		/// <param name="query">The search query.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="query"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uids"/> is empty.</para>
		/// <para>-or-</para>
		/// <para>One or more of the <paramref name="uids"/> is invalid.</para>
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>One or more search terms in the <paramref name="query"/> are not supported.</para>
		/// <para>-or-</para>
		/// <para>The server does not support the specified search options.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual SearchResults Search (SearchOptions options, IList<UniqueId> uids, SearchQuery query, CancellationToken cancellationToken = default)
		{
			var uidSet = new UidSearchQuery (uids);

			if (query == null)
				throw new ArgumentNullException (nameof (query));

			return Search (options, uidSet.And (query), cancellationToken);
		}

		/// <summary>
		/// Asynchronously search the subset of UIDs in the folder for messages matching the specified query.
		/// </summary>
		/// <remarks>
		/// Asynchronously searches the subset of UIDs in the folder for messages matching the specified query,
		/// returning only the specified search results.
		/// </remarks>
		/// <returns>The search results.</returns>
		/// <param name="options">The search options.</param>
		/// <param name="uids">The subset of UIDs</param>
		/// <param name="query">The search query.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="query"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uids"/> is empty.</para>
		/// <para>-or-</para>
		/// <para>One or more of the <paramref name="uids"/> is invalid.</para>
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>One or more search terms in the <paramref name="query"/> are not supported.</para>
		/// <para>-or-</para>
		/// <para>The server does not support the specified search options.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task<SearchResults> SearchAsync (SearchOptions options, IList<UniqueId> uids, SearchQuery query, CancellationToken cancellationToken = default)
		{
			var uidSet = new UidSearchQuery (uids);

			if (query == null)
				throw new ArgumentNullException (nameof (query));

			return SearchAsync (options, uidSet.And (query), cancellationToken);
		}

		/// <summary>
		/// Sort messages matching the specified query.
		/// </summary>
		/// <remarks>
		/// The returned array of unique identifiers will be sorted in the preferred order and
		/// can be used with <see cref="IMailFolder.GetMessage(UniqueId,CancellationToken,ITransferProgress)"/>.
		/// </remarks>
		/// /// <returns>An array of matching UIDs in the specified sort order.</returns>
		/// <param name="query">The search query.</param>
		/// <param name="orderBy">The sort order.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="query"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="orderBy"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="orderBy"/> is empty.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>One or more search terms in the <paramref name="query"/> are not supported.</para>
		/// <para>-or-</para>
		/// <para>The server does not support sorting search results.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual IList<UniqueId> Sort (SearchQuery query, IList<OrderBy> orderBy, CancellationToken cancellationToken = default)
		{
			var results = Sort (SearchOptions.None, query, orderBy, cancellationToken);

			return results.UniqueIds;
		}

		/// <summary>
		/// Asynchronously sort messages matching the specified query.
		/// </summary>
		/// <remarks>
		/// The returned array of unique identifiers will be sorted in the preferred order and
		/// can be used with <see cref="IMailFolder.GetMessage(UniqueId,CancellationToken,ITransferProgress)"/>.
		/// </remarks>
		/// <returns>An array of matching UIDs in the specified sort order.</returns>
		/// <param name="query">The search query.</param>
		/// <param name="orderBy">The sort order.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="query"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="orderBy"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="orderBy"/> is empty.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>One or more search terms in the <paramref name="query"/> are not supported.</para>
		/// <para>-or-</para>
		/// <para>The server does not support sorting search results.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual async Task<IList<UniqueId>> SortAsync (SearchQuery query, IList<OrderBy> orderBy, CancellationToken cancellationToken = default)
		{
			var results = await SortAsync (SearchOptions.None, query, orderBy, cancellationToken).ConfigureAwait (false);

			return results.UniqueIds;
		}

		/// <summary>
		/// Sort messages matching the specified query.
		/// </summary>
		/// <remarks>
		/// The returned array of unique identifiers will be sorted in the preferred order and
		/// can be used with <see cref="IMailFolder.GetMessage(UniqueId,CancellationToken,ITransferProgress)"/>.
		/// </remarks>
		/// <returns>An array of matching UIDs in the specified sort order.</returns>
		/// <param name="uids">The subset of UIDs</param>
		/// <param name="query">The search query.</param>
		/// <param name="orderBy">The sort order.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="query"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="orderBy"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uids"/> is empty.</para>
		/// <para>-or-</para>
		/// <para>One or more of the <paramref name="uids"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para><paramref name="orderBy"/> is empty.</para>
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>One or more search terms in the <paramref name="query"/> are not supported.</para>
		/// <para>-or-</para>
		/// <para>The server does not support sorting search results.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual IList<UniqueId> Sort (IList<UniqueId> uids, SearchQuery query, IList<OrderBy> orderBy, CancellationToken cancellationToken = default)
		{
			var uidSet = new UidSearchQuery (uids);

			if (query == null)
				throw new ArgumentNullException (nameof (query));

			return Sort (uidSet.And (query), orderBy, cancellationToken);
		}

		/// <summary>
		/// Asynchronously sort messages matching the specified query.
		/// </summary>
		/// <remarks>
		/// The returned array of unique identifiers will be sorted in the preferred order and
		/// can be used with <see cref="IMailFolder.GetMessage(UniqueId,CancellationToken,ITransferProgress)"/>.
		/// </remarks>
		/// <returns>An array of matching UIDs in the specified sort order.</returns>
		/// <param name="uids">The subset of UIDs</param>
		/// <param name="query">The search query.</param>
		/// <param name="orderBy">The sort order.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="query"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="orderBy"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uids"/> is empty.</para>
		/// <para>-or-</para>
		/// <para>One or more of the <paramref name="uids"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para><paramref name="orderBy"/> is empty.</para>
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>One or more search terms in the <paramref name="query"/> are not supported.</para>
		/// <para>-or-</para>
		/// <para>The server does not support sorting search results.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task<IList<UniqueId>> SortAsync (IList<UniqueId> uids, SearchQuery query, IList<OrderBy> orderBy, CancellationToken cancellationToken = default)
		{
			var uidSet = new UidSearchQuery (uids);

			if (query == null)
				throw new ArgumentNullException (nameof (query));

			return SortAsync (uidSet.And (query), orderBy, cancellationToken);
		}

		/// <summary>
		/// Sort messages matching the specified query.
		/// </summary>
		/// <remarks>
		/// Searches the folder for messages matching the specified query, returning the search results in the specified sort order.
		/// </remarks>
		/// <returns>The search results.</returns>
		/// <param name="options">The search options.</param>
		/// <param name="query">The search query.</param>
		/// <param name="orderBy">The sort order.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="query"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="orderBy"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="orderBy"/> is empty.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>One or more search terms in the <paramref name="query"/> are not supported.</para>
		/// <para>-or-</para>
		/// <para>The server does not support the specified search options.</para>
		/// <para>-or-</para>
		/// <para>The server does not support sorting search results.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="MailFolder"/> is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract SearchResults Sort (SearchOptions options, SearchQuery query, IList<OrderBy> orderBy, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously sort messages matching the specified query.
		/// </summary>
		/// <remarks>
		/// Asynchronously searches the folder for messages matching the specified query, returning the search results in the specified sort order.
		/// </remarks>
		/// <returns>The search results.</returns>
		/// <param name="options">The search options.</param>
		/// <param name="query">The search query.</param>
		/// <param name="orderBy">The sort order.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="query"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="orderBy"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <paramref name="orderBy"/> is empty.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>One or more search terms in the <paramref name="query"/> are not supported.</para>
		/// <para>-or-</para>
		/// <para>The server does not support the specified search options.</para>
		/// <para>-or-</para>
		/// <para>The server does not support sorting search results.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The <see cref="MailFolder"/> is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<SearchResults> SortAsync (SearchOptions options, SearchQuery query, IList<OrderBy> orderBy, CancellationToken cancellationToken = default);

		/// <summary>
		/// Sort messages matching the specified query.
		/// </summary>
		/// <remarks>
		/// Searches the folder for messages matching the specified query, returning the search results in the specified sort order.
		/// </remarks>
		/// <returns>The search results.</returns>
		/// <param name="options">The search options.</param>
		/// <param name="uids">The subset of UIDs</param>
		/// <param name="query">The search query.</param>
		/// <param name="orderBy">The sort order.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="query"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="orderBy"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uids"/> is empty.</para>
		/// <para>-or-</para>
		/// <para>One or more of the <paramref name="uids"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para><paramref name="orderBy"/> is empty.</para>
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>One or more search terms in the <paramref name="query"/> are not supported.</para>
		/// <para>-or-</para>
		/// <para>The server does not support the specified search options.</para>
		/// <para>-or-</para>
		/// <para>The server does not support sorting search results.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual SearchResults Sort (SearchOptions options, IList<UniqueId> uids, SearchQuery query, IList<OrderBy> orderBy, CancellationToken cancellationToken = default)
		{
			var uidSet = new UidSearchQuery (uids);

			if (query == null)
				throw new ArgumentNullException (nameof (query));

			return Sort (options, uidSet.And (query), orderBy, cancellationToken);
		}

		/// <summary>
		/// Asynchronously sort messages matching the specified query, returning the search results in the specified sort order.
		/// </summary>
		/// <remarks>
		/// Asynchronously searches the folder for messages matching the specified query,
		/// returning the search results in the specified sort order.
		/// </remarks>
		/// <returns>The search results.</returns>
		/// <param name="options">The search options.</param>
		/// <param name="uids">The subset of UIDs</param>
		/// <param name="query">The search query.</param>
		/// <param name="orderBy">The sort order.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="query"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="orderBy"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uids"/> is empty.</para>
		/// <para>-or-</para>
		/// <para>One or more of the <paramref name="uids"/> is invalid.</para>
		/// <para>-or-</para>
		/// <para><paramref name="orderBy"/> is empty.</para>
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>One or more search terms in the <paramref name="query"/> are not supported.</para>
		/// <para>-or-</para>
		/// <para>The server does not support the specified search options.</para>
		/// <para>-or-</para>
		/// <para>The server does not support sorting search results.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public virtual Task<SearchResults> SortAsync (SearchOptions options, IList<UniqueId> uids, SearchQuery query, IList<OrderBy> orderBy, CancellationToken cancellationToken = default)
		{
			var uidSet = new UidSearchQuery (uids);

			if (query == null)
				throw new ArgumentNullException (nameof (query));

			return SortAsync (options, uidSet.And (query), orderBy, cancellationToken);
		}

		/// <summary>
		/// Thread the messages in the folder that match the search query using the specified threading algorithm.
		/// </summary>
		/// <remarks>
		/// The <see cref="MessageThread.UniqueId"/> can be used with methods such as
		/// <see cref="IMailFolder.GetMessage(UniqueId,CancellationToken,ITransferProgress)"/>.
		/// </remarks>
		/// <returns>An array of message threads.</returns>
		/// <param name="algorithm">The threading algorithm to use.</param>
		/// <param name="query">The search query.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="algorithm"/> is not supported.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="query"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>One or more search terms in the <paramref name="query"/> are not supported.</para>
		/// <para>-or-</para>
		/// <para>The server does not support threading search results.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract IList<MessageThread> Thread (ThreadingAlgorithm algorithm, SearchQuery query, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously thread the messages in the folder that match the search query using the specified threading algorithm.
		/// </summary>
		/// <remarks>
		/// The <see cref="MessageThread.UniqueId"/> can be used with methods such as
		/// <see cref="IMailFolder.GetMessage(UniqueId,CancellationToken,ITransferProgress)"/>.
		/// </remarks>
		/// <returns>An array of message threads.</returns>
		/// <param name="algorithm">The threading algorithm to use.</param>
		/// <param name="query">The search query.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="algorithm"/> is not supported.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="query"/> is <see langword="null" />.
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>One or more search terms in the <paramref name="query"/> are not supported.</para>
		/// <para>-or-</para>
		/// <para>The server does not support threading search results.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<IList<MessageThread>> ThreadAsync (ThreadingAlgorithm algorithm, SearchQuery query, CancellationToken cancellationToken = default);

		/// <summary>
		/// Thread the messages in the folder that match the search query using the specified threading algorithm.
		/// </summary>
		/// <remarks>
		/// The <see cref="MessageThread.UniqueId"/> can be used with methods such as
		/// <see cref="IMailFolder.GetMessage(UniqueId,CancellationToken,ITransferProgress)"/>.
		/// </remarks>
		/// <returns>An array of message threads.</returns>
		/// <param name="uids">The subset of UIDs</param>
		/// <param name="algorithm">The threading algorithm to use.</param>
		/// <param name="query">The search query.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="algorithm"/> is not supported.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="query"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uids"/> is empty.</para>
		/// <para>-or-</para>
		/// <para>One or more of the <paramref name="uids"/> is invalid.</para>
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>One or more search terms in the <paramref name="query"/> are not supported.</para>
		/// <para>-or-</para>
		/// <para>The server does not support threading search results.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract IList<MessageThread> Thread (IList<UniqueId> uids, ThreadingAlgorithm algorithm, SearchQuery query, CancellationToken cancellationToken = default);

		/// <summary>
		/// Asynchronously thread the messages in the folder that match the search query using the specified threading algorithm.
		/// </summary>
		/// <remarks>
		/// The <see cref="MessageThread.UniqueId"/> can be used with methods such as
		/// <see cref="IMailFolder.GetMessage(UniqueId,CancellationToken,ITransferProgress)"/>.
		/// </remarks>
		/// <returns>An array of message threads.</returns>
		/// <param name="uids">The subset of UIDs</param>
		/// <param name="algorithm">The threading algorithm to use.</param>
		/// <param name="query">The search query.</param>
		/// <param name="cancellationToken">The cancellation token.</param>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="algorithm"/> is not supported.
		/// </exception>
		/// <exception cref="System.ArgumentNullException">
		/// <para><paramref name="uids"/> is <see langword="null" />.</para>
		/// <para>-or-</para>
		/// <para><paramref name="query"/> is <see langword="null" />.</para>
		/// </exception>
		/// <exception cref="System.ArgumentException">
		/// <para><paramref name="uids"/> is empty.</para>
		/// <para>-or-</para>
		/// <para>One or more of the <paramref name="uids"/> is invalid.</para>
		/// </exception>
		/// <exception cref="System.NotSupportedException">
		/// <para>One or more search terms in the <paramref name="query"/> are not supported.</para>
		/// <para>-or-</para>
		/// <para>The server does not support threading search results.</para>
		/// </exception>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		/// <exception cref="System.OperationCanceledException">
		/// The operation was canceled via the cancellation token.
		/// </exception>
		/// <exception cref="System.IO.IOException">
		/// An I/O error occurred.
		/// </exception>
		/// <exception cref="ProtocolException">
		/// The server's response contained unexpected tokens.
		/// </exception>
		/// <exception cref="CommandException">
		/// The command failed.
		/// </exception>
		public abstract Task<IList<MessageThread>> ThreadAsync (IList<UniqueId> uids, ThreadingAlgorithm algorithm, SearchQuery query, CancellationToken cancellationToken = default);

		/// <summary>
		/// Occurs when the folder is opened.
		/// </summary>
		/// <remarks>
		/// The <see cref="Opened"/> event is emitted when the folder is opened.
		/// </remarks>
		public event EventHandler<EventArgs> Opened;

		/// <summary>
		/// Raise the opened event.
		/// </summary>
		/// <remarks>
		/// Raises the opened event.
		/// </remarks>
		protected virtual void OnOpened ()
		{
			Opened?.Invoke (this, EventArgs.Empty);
		}

		/// <summary>
		/// Occurs when the folder is closed.
		/// </summary>
		/// <remarks>
		/// The <see cref="Closed"/> event is emitted when the folder is closed.
		/// </remarks>
		public event EventHandler<EventArgs> Closed;

		/// <summary>
		/// Raise the closed event.
		/// </summary>
		/// <remarks>
		/// Raises the closed event.
		/// </remarks>
		internal protected virtual void OnClosed ()
		{
			PermanentFlags = MessageFlags.None;
			AcceptedFlags = MessageFlags.None;
			Access = FolderAccess.None;
			FirstUnread = -1;

			AnnotationAccess = AnnotationAccess.None;
			AnnotationScopes = AnnotationScope.None;
			MaxAnnotationSize = 0;

			Closed?.Invoke (this, EventArgs.Empty);
		}

		/// <summary>
		/// Occurs when the folder is deleted.
		/// </summary>
		/// <remarks>
		/// The <see cref="Deleted"/> event is emitted when the folder is deleted.
		/// </remarks>
		public event EventHandler<EventArgs> Deleted;

		/// <summary>
		/// Raise the deleted event.
		/// </summary>
		/// <remarks>
		/// Raises the deleted event.
		/// </remarks>
		protected virtual void OnDeleted ()
		{
			Deleted?.Invoke (this, EventArgs.Empty);
		}

		/// <summary>
		/// Occurs when the folder is renamed.
		/// </summary>
		/// <remarks>
		/// The <see cref="Renamed"/> event is emitted when the folder is renamed.
		/// </remarks>
		public event EventHandler<FolderRenamedEventArgs> Renamed;

		/// <summary>
		/// Raise the renamed event.
		/// </summary>
		/// <remarks>
		/// Raises the renamed event.
		/// </remarks>
		/// <param name="oldName">The old name of the folder.</param>
		/// <param name="newName">The new name of the folder.</param>
		protected virtual void OnRenamed (string oldName, string newName)
		{
			Renamed?.Invoke (this, new FolderRenamedEventArgs (oldName, newName));
		}

		/// <summary>
		/// Notifies the folder that a parent folder has been renamed.
		/// </summary>
		/// <remarks>
		/// <see cref="IMailFolder"/> implementations should override this method
		/// to update their state (such as updating their <see cref="FullName"/>
		/// property).
		/// </remarks>
		protected virtual void OnParentFolderRenamed ()
		{
		}

		void OnParentFolderRenamed (object sender, FolderRenamedEventArgs e)
		{
			var oldFullName = FullName;

			OnParentFolderRenamed ();

			if (FullName != oldFullName)
				OnRenamed (oldFullName, FullName);
		}

		/// <summary>
		/// Occurs when the folder is subscribed.
		/// </summary>
		/// <remarks>
		/// The <see cref="Subscribed"/> event is emitted when the folder is subscribed.
		/// </remarks>
		public event EventHandler<EventArgs> Subscribed;

		/// <summary>
		/// Raise the subscribed event.
		/// </summary>
		/// <remarks>
		/// Raises the subscribed event.
		/// </remarks>
		protected virtual void OnSubscribed ()
		{
			Subscribed?.Invoke (this, EventArgs.Empty);
		}

		/// <summary>
		/// Occurs when the folder is unsubscribed.
		/// </summary>
		/// <remarks>
		/// The <see cref="Unsubscribed"/> event is emitted when the folder is unsubscribed.
		/// </remarks>
		public event EventHandler<EventArgs> Unsubscribed;

		/// <summary>
		/// Raise the unsubscribed event.
		/// </summary>
		/// <remarks>
		/// Raises the unsubscribed event.
		/// </remarks>
		protected virtual void OnUnsubscribed ()
		{
			Unsubscribed?.Invoke (this, EventArgs.Empty);
		}

		/// <summary>
		/// Occurs when a message is expunged from the folder.
		/// </summary>
		/// <remarks>
		/// The <see cref="MessageExpunged"/> event is emitted when a message is expunged from the folder.
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapIdleExample.cs"/>
		/// </example>
		public event EventHandler<MessageEventArgs> MessageExpunged;

		/// <summary>
		/// Raise the message expunged event.
		/// </summary>
		/// <remarks>
		/// Raises the message expunged event.
		/// </remarks>
		/// <param name="args">The message expunged event args.</param>
		protected virtual void OnMessageExpunged (MessageEventArgs args)
		{
			MessageExpunged?.Invoke (this, args);
		}

		/// <summary>
		/// Occurs when a message vanishes from the folder.
		/// </summary>
		/// <remarks>
		/// The <see cref="MessagesVanished"/> event is emitted when messages vanish from the folder.
		/// </remarks>
		public event EventHandler<MessagesVanishedEventArgs> MessagesVanished;

		/// <summary>
		/// Raise the messages vanished event.
		/// </summary>
		/// <remarks>
		/// Raises the messages vanished event.
		/// </remarks>
		/// <param name="args">The messages vanished event args.</param>
		protected virtual void OnMessagesVanished (MessagesVanishedEventArgs args)
		{
			MessagesVanished?.Invoke (this, args);
		}

		/// <summary>
		/// Occurs when flags changed on a message.
		/// </summary>
		/// <remarks>
		/// The <see cref="MessageFlagsChanged"/> event is emitted when the flags for a message are changed.
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapIdleExample.cs"/>
		/// </example>
		public event EventHandler<MessageFlagsChangedEventArgs> MessageFlagsChanged;

		/// <summary>
		/// Raise the message flags changed event.
		/// </summary>
		/// <remarks>
		/// Raises the message flags changed event.
		/// </remarks>
		/// <param name="args">The message flags changed event args.</param>
		protected virtual void OnMessageFlagsChanged (MessageFlagsChangedEventArgs args)
		{
			MessageFlagsChanged?.Invoke (this, args);
		}

		/// <summary>
		/// Occurs when labels changed on a message.
		/// </summary>
		/// <remarks>
		/// The <see cref="MessageLabelsChanged"/> event is emitted when the labels for a message are changed.
		/// </remarks>
		public event EventHandler<MessageLabelsChangedEventArgs> MessageLabelsChanged;

		/// <summary>
		/// Raise the message labels changed event.
		/// </summary>
		/// <remarks>
		/// Raises the message labels changed event.
		/// </remarks>
		/// <param name="args">The message labels changed event args.</param>
		protected virtual void OnMessageLabelsChanged (MessageLabelsChangedEventArgs args)
		{
			MessageLabelsChanged?.Invoke (this, args);
		}

		/// <summary>
		/// Occurs when annotations changed on a message.
		/// </summary>
		/// <remarks>
		/// The <see cref="AnnotationsChanged"/> event is emitted when the annotations for a message are changed.
		/// </remarks>
		public event EventHandler<AnnotationsChangedEventArgs> AnnotationsChanged;

		/// <summary>
		/// Raise the message annotations changed event.
		/// </summary>
		/// <remarks>
		/// Raises the message annotations changed event.
		/// </remarks>
		/// <param name="args">The message annotations changed event args.</param>
		protected virtual void OnAnnotationsChanged (AnnotationsChangedEventArgs args)
		{
			AnnotationsChanged?.Invoke (this, args);
		}

		/// <summary>
		/// Occurs when a message summary is fetched from the folder.
		/// </summary>
		/// <remarks>
		/// <para>Emitted when a message summary is fetched from the folder.</para>
		/// <para>When multiple message summaries are being fetched from a remote folder,
		/// it is possible that the connection will drop or some other exception will
		/// occur, causing the Fetch method to fail and lose all of the data that has been
		/// downloaded up to that point, requiring the client to request the same set of
		/// message summaries all over again after it reconnects. This is obviously
		/// inefficient. To alleviate this potential problem, this event will be emitted
		/// as soon as the <see cref="IMailFolder"/> successfully parses each untagged FETCH
		/// response from the server, allowing the client to commit this data immediately to
		/// its local cache.</para>
		/// <note type="note">Depending on the IMAP server, it is possible that the
		/// <see cref="MessageSummaryFetched"/> event will be emitted for the same message
		/// multiple times if the IMAP server happens to split the requested fields into
		/// multiple untagged FETCH responses. Use the <see cref="IMessageSummary.Fields"/>
		/// property to determine which f<see cref="IMessageSummary"/> properties have
		/// been populated.</note>
		/// </remarks>
		public event EventHandler<MessageSummaryFetchedEventArgs> MessageSummaryFetched;

		/// <summary>
		/// Raise the message summary fetched event.
		/// </summary>
		/// <remarks>
		/// <para>Raises the message summary fetched event.</para>
		/// <para>When multiple message summaries are being fetched from a remote folder,
		/// it is possible that the connection will drop or some other exception will
		/// occur, causing the Fetch method to fail and lose all of the data that has been
		/// downloaded up to that point, requiring the client to request the same set of
		/// message summaries all over again after it reconnects. This is obviously
		/// inefficient. To alleviate this potential problem, this event will be emitted
		/// as soon as the <see cref="IMailFolder"/> successfully parses each untagged FETCH
		/// response from the server, allowing the client to commit this data immediately to
		/// its local cache.</para>
		/// <note type="note">Depending on the IMAP server, it is possible that
		/// <see cref="OnMessageSummaryFetched"/> will be invoked for the same message
		/// multiple times if the IMAP server happens to split the requested fields into
		/// multiple untagged FETCH responses. Use the <see cref="IMessageSummary.Fields"/>
		/// property to determine which f<see cref="IMessageSummary"/> properties have
		/// been populated.</note>
		/// </remarks>
		/// <param name="message">The message summary.</param>
		protected virtual void OnMessageSummaryFetched (IMessageSummary message)
		{
			MessageSummaryFetched?.Invoke (this, new MessageSummaryFetchedEventArgs (message));
		}

		/// <summary>
		/// Occurs when metadata changes.
		/// </summary>
		/// <remarks>
		/// The <see cref="MetadataChanged"/> event is emitted when metadata changes.
		/// </remarks>
		public event EventHandler<MetadataChangedEventArgs> MetadataChanged;

		/// <summary>
		/// Raise the metadata changed event.
		/// </summary>
		/// <remarks>
		/// Raises the metadata changed event.
		/// </remarks>
		/// <param name="metadata">The metadata that changed.</param>
		internal protected virtual void OnMetadataChanged (Metadata metadata)
		{
			MetadataChanged?.Invoke (this, new MetadataChangedEventArgs (metadata));
		}

		/// <summary>
		/// Occurs when the mod-sequence changed on a message.
		/// </summary>
		/// <remarks>
		/// The <see cref="ModSeqChanged"/> event is emitted when the mod-sequence for a message is changed.
		/// </remarks>
		public event EventHandler<ModSeqChangedEventArgs> ModSeqChanged;

		/// <summary>
		/// Raise the message mod-sequence changed event.
		/// </summary>
		/// <remarks>
		/// Raises the message mod-sequence changed event.
		/// </remarks>
		/// <param name="args">The mod-sequence changed event args.</param>
		protected virtual void OnModSeqChanged (ModSeqChangedEventArgs args)
		{
			ModSeqChanged?.Invoke (this, args);
		}

		/// <summary>
		/// Occurs when the highest mod-sequence changes.
		/// </summary>
		/// <remarks>
		/// The <see cref="HighestModSeqChanged"/> event is emitted whenever the <see cref="HighestModSeq"/> value changes.
		/// </remarks>
		public event EventHandler<EventArgs> HighestModSeqChanged;

		/// <summary>
		/// Raise the highest mod-sequence changed event.
		/// </summary>
		/// <remarks>
		/// Raises the highest mod-sequence changed event.
		/// </remarks>
		protected virtual void OnHighestModSeqChanged ()
		{
			HighestModSeqChanged?.Invoke (this, EventArgs.Empty);
		}

		/// <summary>
		/// Occurs when the next UID changes.
		/// </summary>
		/// <remarks>
		/// The <see cref="UidNextChanged"/> event is emitted whenever the <see cref="UidNext"/> value changes.
		/// </remarks>
		public event EventHandler<EventArgs> UidNextChanged;

		/// <summary>
		/// Raise the next UID changed event.
		/// </summary>
		/// <remarks>
		/// Raises the next UID changed event.
		/// </remarks>
		protected virtual void OnUidNextChanged ()
		{
			UidNextChanged?.Invoke (this, EventArgs.Empty);
		}

		/// <summary>
		/// Occurs when the UID validity changes.
		/// </summary>
		/// <remarks>
		/// The <see cref="UidValidityChanged"/> event is emitted whenever the <see cref="UidValidity"/> value changes.
		/// </remarks>
		public event EventHandler<EventArgs> UidValidityChanged;

		/// <summary>
		/// Raise the uid validity changed event.
		/// </summary>
		/// <remarks>
		/// Raises the uid validity changed event.
		/// </remarks>
		protected virtual void OnUidValidityChanged ()
		{
			UidValidityChanged?.Invoke (this, EventArgs.Empty);
		}

		/// <summary>
		/// Occurs when the folder ID changes.
		/// </summary>
		/// <remarks>
		/// The <see cref="IdChanged"/> event is emitted whenever the <see cref="Id"/> value changes.
		/// </remarks>
		public event EventHandler<EventArgs> IdChanged;

		/// <summary>
		/// Raise the ID changed event.
		/// </summary>
		/// <remarks>
		/// Raises the ID changed event.
		/// </remarks>
		protected virtual void OnIdChanged ()
		{
			IdChanged?.Invoke (this, EventArgs.Empty);
		}

		/// <summary>
		/// Occurs when the folder size changes.
		/// </summary>
		/// <remarks>
		/// The <see cref="SizeChanged"/> event is emitted whenever the <see cref="Size"/> value changes.
		/// </remarks>
		public event EventHandler<EventArgs> SizeChanged;

		/// <summary>
		/// Raise the size changed event.
		/// </summary>
		/// <remarks>
		/// Raises the size changed event.
		/// </remarks>
		protected virtual void OnSizeChanged ()
		{
			SizeChanged?.Invoke (this, EventArgs.Empty);
		}

		/// <summary>
		/// Occurs when the message count changes.
		/// </summary>
		/// <remarks>
		/// The <see cref="CountChanged"/> event is emitted whenever the <see cref="Count"/> value changes.
		/// </remarks>
		/// <example>
		/// <code language="c#" source="Examples\ImapIdleExample.cs"/>
		/// </example>
		public event EventHandler<EventArgs> CountChanged;

		/// <summary>
		/// Raise the message count changed event.
		/// </summary>
		/// <remarks>
		/// Raises the message count changed event.
		/// </remarks>
		protected virtual void OnCountChanged ()
		{
			CountChanged?.Invoke (this, EventArgs.Empty);
		}

		/// <summary>
		/// Occurs when the recent message count changes.
		/// </summary>
		/// <remarks>
		/// The <see cref="RecentChanged"/> event is emitted whenever the <see cref="Recent"/> value changes.
		/// </remarks>
		public event EventHandler<EventArgs> RecentChanged;

		/// <summary>
		/// Raise the recent message count changed event.
		/// </summary>
		/// <remarks>
		/// Raises the recent message count changed event.
		/// </remarks>
		protected virtual void OnRecentChanged ()
		{
			RecentChanged?.Invoke (this, EventArgs.Empty);
		}

		/// <summary>
		/// Occurs when the unread message count changes.
		/// </summary>
		/// <remarks>
		/// The <see cref="UnreadChanged"/> event is emitted whenever the <see cref="Unread"/> value changes.
		/// </remarks>
		public event EventHandler<EventArgs> UnreadChanged;

		/// <summary>
		/// Raise the unread message count changed event.
		/// </summary>
		/// <remarks>
		/// Raises the unread message count changed event.
		/// </remarks>
		protected virtual void OnUnreadChanged ()
		{
			UnreadChanged?.Invoke (this, EventArgs.Empty);
		}

		#region IEnumerable<MimeMessage> implementation

		/// <summary>
		/// Get an enumerator for the messages in the folder.
		/// </summary>
		/// <remarks>
		/// Gets an enumerator for the messages in the folder.
		/// </remarks>
		/// <returns>The enumerator.</returns>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		public abstract IEnumerator<MimeMessage> GetEnumerator ();

		/// <summary>
		/// Get an enumerator for the messages in the folder.
		/// </summary>
		/// <remarks>
		/// Gets an enumerator for the messages in the folder.
		/// </remarks>
		/// <returns>The enumerator.</returns>
		/// <exception cref="System.ObjectDisposedException">
		/// The <see cref="IMailStore"/> has been disposed.
		/// </exception>
		/// <exception cref="ServiceNotConnectedException">
		/// The <see cref="IMailStore"/> is not connected.
		/// </exception>
		/// <exception cref="ServiceNotAuthenticatedException">
		/// The <see cref="IMailStore"/> is not authenticated.
		/// </exception>
		/// <exception cref="FolderNotOpenException">
		/// The folder is not currently open.
		/// </exception>
		IEnumerator IEnumerable.GetEnumerator ()
		{
			return GetEnumerator ();
		}

		#endregion

		/// <summary>
		/// Returns a <see cref="System.String"/> that represents the current <see cref="MailKit.MailFolder"/>.
		/// </summary>
		/// <remarks>
		/// Returns a <see cref="System.String"/> that represents the current <see cref="MailKit.MailFolder"/>.
		/// </remarks>
		/// <returns>A <see cref="System.String"/> that represents the current <see cref="MailKit.MailFolder"/>.</returns>
		public override string ToString ()
		{
			return FullName;
		}
	}
}
