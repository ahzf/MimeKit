//
// MimeUtils.cs
//
// Author: Jeffrey Stedfast <jeff@xamarin.com>
//
// Copyright (c) 2013-2014 Xamarin Inc. (www.xamarin.com)
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
using System.Net;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;

namespace MimeKit.Utils {
	/// <summary>
	/// MIME utility methods.
	/// </summary>
	/// <remarks>
	/// Various utility methods that don't belong anywhere else.
	/// </remarks>
	public static class MimeUtils
	{
		static readonly Random random = new Random ((int) DateTime.Now.Ticks);

		/// <summary>
		/// Generates a Message-Id.
		/// </summary>
		/// <remarks>
		/// Generates a new Message-Id using the supplied domain.
		/// </remarks>
		/// <returns>The message identifier.</returns>
		/// <param name="domain">A domain to use.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="domain"/> is <c>null</c>.
		/// </exception>
		public static string GenerateMessageId (string domain)
		{
			if (domain == null)
				throw new ArgumentNullException ("domain");

			var guid = new byte[16];

			lock (random) {
				random.NextBytes (guid);
			}

			return string.Format ("{0}@{1}", new Guid (guid), domain);
		}

		/// <summary>
		/// Generates a Message-Id.
		/// </summary>
		/// <remarks>
		/// Generates a new Message-Id using the local machine's domain.
		/// </remarks>
		/// <returns>The message identifier.</returns>
		public static string GenerateMessageId ()
		{
#if PORTABLE
			return GenerateMessageId ("localhost.localdomain");
#else
			return GenerateMessageId (Dns.GetHostName ());
#endif
		}

		/// <summary>
		/// Enumerates the message-id references such as those that can be found in
		/// the In-Reply-To or References header.
		/// </summary>
		/// <remarks>
		/// Incrementally parses Message-Ids (such as those from a References header
		/// in a MIME message) from the supplied buffer starting at the given index
		/// and spanning across the specified number of bytes.
		/// </remarks>
		/// <returns>The references.</returns>
		/// <param name="buffer">The raw byte buffer to parse.</param>
		/// <param name="startIndex">The index into the buffer to start parsing.</param>
		/// <param name="length">The length of the buffer to parse.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="buffer"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="startIndex"/> and <paramref name="length"/> do not specify
		/// a valid range in the byte array.
		/// </exception>
		public static IEnumerable<string> EnumerateReferences (byte[] buffer, int startIndex, int length)
		{
			byte[] sentinels = { (byte) '>' };
			int endIndex = startIndex + length;
			int index = startIndex;
			InternetAddress addr;
			string msgid;

			if (buffer == null)
				throw new ArgumentNullException ("buffer");

			if (startIndex < 0 || startIndex > buffer.Length)
				throw new ArgumentOutOfRangeException ("startIndex");

			if (length < 0 || length > (buffer.Length - startIndex))
				throw new ArgumentOutOfRangeException ("length");

			do {
				if (!ParseUtils.SkipCommentsAndWhiteSpace (buffer, ref index, endIndex, false))
					break;

				if (index >= endIndex)
					break;

				if (buffer[index] == '<') {
					if (!InternetAddress.TryParseMailbox (ParserOptions.Default, buffer, startIndex, ref index, endIndex, "", 65001, false, out addr))
						break;

					msgid = ((MailboxAddress) addr).Address;

					// Note: some message-id's are broken and in the form local-part@domain@domain
					// https://github.com/jstedfast/MailKit/issues/138
					while (index < endIndex && buffer[index] == (byte) '@') {
						int saved = index;
						string domain;

						index++;

						if (!ParseUtils.TryParseDomain (buffer, ref index, endIndex, sentinels, false, out domain)) {
							index = saved;
							break;
						}

						msgid += "@" + domain;
					}

					yield return msgid;
				} else if (!ParseUtils.SkipWord (buffer, ref index, endIndex, false)) {
					index++;
				}
			} while (index < endIndex);

			yield break;
		}

		/// <summary>
		/// Enumerates the message-id references such as those that can be found in
		/// the In-Reply-To or References header.
		/// </summary>
		/// <remarks>
		/// Incrementally parses Message-Ids (such as those from a References header
		/// in a MIME message) from the specified text.
		/// </remarks>
		/// <returns>The references.</returns>
		/// <param name="text">The text to parse.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="text"/> is <c>null</c>.
		/// </exception>
		public static IEnumerable<string> EnumerateReferences (string text)
		{
			if (text == null)
				throw new ArgumentNullException ("text");

			var buffer = Encoding.UTF8.GetBytes (text);

			return EnumerateReferences (buffer, 0, buffer.Length);
		}

		/// <summary>
		/// Tries to parse a version from a header such as Mime-Version.
		/// </summary>
		/// <remarks>
		/// Parses a MIME version string from the supplied buffer starting at the given index
		/// and spanning across the specified number of bytes.
		/// </remarks>
		/// <returns><c>true</c>, if the version was successfully parsed, <c>false</c> otherwise.</returns>
		/// <param name="buffer">The raw byte buffer to parse.</param>
		/// <param name="startIndex">The index into the buffer to start parsing.</param>
		/// <param name="length">The length of the buffer to parse.</param>
		/// <param name="version">The parsed version.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="buffer"/> is <c>null</c>.
		/// </exception>
		/// <exception cref="System.ArgumentOutOfRangeException">
		/// <paramref name="startIndex"/> and <paramref name="length"/> do not specify
		/// a valid range in the byte array.
		/// </exception>
		public static bool TryParseVersion (byte[] buffer, int startIndex, int length, out Version version)
		{
			if (buffer == null)
				throw new ArgumentNullException ("buffer");

			if (startIndex < 0 || startIndex > buffer.Length)
				throw new ArgumentOutOfRangeException ("startIndex");

			if (length < 0 || length > (buffer.Length - startIndex))
				throw new ArgumentOutOfRangeException ("length");

			List<int> values = new List<int> ();
			int endIndex = startIndex + length;
			int index = startIndex;
			int value;

			version = null;

			do {
				if (!ParseUtils.SkipCommentsAndWhiteSpace (buffer, ref index, endIndex, false) || index >= endIndex)
					return false;

				if (!ParseUtils.TryParseInt32 (buffer, ref index, endIndex, out value))
					return false;

				values.Add (value);

				if (!ParseUtils.SkipCommentsAndWhiteSpace (buffer, ref index, endIndex, false))
					return false;

				if (index >= endIndex)
					break;

				if (buffer[index++] != (byte) '.')
					return false;
			} while (index < endIndex);

			switch (values.Count) {
			case 4: version = new Version (values[0], values[1], values[2], values[3]); break;
			case 3: version = new Version (values[0], values[1], values[2]); break;
			case 2: version = new Version (values[0], values[1]); break;
			default: return false;
			}

			return true;
		}

		/// <summary>
		/// Tries to parse a version from a header such as Mime-Version.
		/// </summary>
		/// <remarks>
		/// Parses a MIME version string from the specified text.
		/// </remarks>
		/// <returns><c>true</c>, if the version was successfully parsed, <c>false</c> otherwise.</returns>
		/// <param name="text">The text to parse.</param>
		/// <param name="version">The parsed version.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="text"/> is <c>null</c>.
		/// </exception>
		public static bool TryParseVersion (string text, out Version version)
		{
			if (text == null)
				throw new ArgumentNullException ("text");

			var buffer = Encoding.UTF8.GetBytes (text);

			return TryParseVersion (buffer, 0, buffer.Length, out version);
		}

		/// <summary>
		/// Quotes the specified text.
		/// </summary>
		/// <remarks>
		/// Quotes the specified text, enclosing it in double-quotes and escaping
		/// any backslashes and double-quotes within.
		/// </remarks>
		/// <returns>The quoted text.</returns>
		/// <param name="text">The text to quote.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="text"/> is <c>null</c>.
		/// </exception>
		public static string Quote (string text)
		{
			if (text == null)
				throw new ArgumentNullException ("text");

			var quoted = new StringBuilder ();

			quoted.Append ("\"");
			for (int i = 0; i < text.Length; i++) {
				if (text[i] == '\\' || text[i] == '"')
					quoted.Append ('\\');
				quoted.Append (text[i]);
			}
			quoted.Append ("\"");

			return quoted.ToString ();
		}

		/// <summary>
		/// Unquotes the specified text.
		/// </summary>
		/// <remarks>
		/// Unquotes the specified text, removing any escaped backslashes within.
		/// </remarks>
		/// <returns>The unquoted text.</returns>
		/// <param name="text">The text to unquote.</param>
		/// <exception cref="System.ArgumentNullException">
		/// <paramref name="text"/> is <c>null</c>.
		/// </exception>
		public static string Unquote (string text)
		{
			if (text == null)
				throw new ArgumentNullException ("text");

			int index = text.IndexOfAny (new [] { '\r', '\n', '\t', '\\', '"' });

			if (index == -1)
				return text;

			var builder = new StringBuilder ();
			bool escaped = false;
			bool quoted = false;

			for (int i = 0; i < text.Length; i++) {
				switch (text[i]) {
				case '\r':
				case '\n':
					escaped = false;
					break;
				case '\t':
					builder.Append (' ');
					escaped = false;
					break;
				case '\\':
					if (escaped)
						builder.Append ('\\');
					escaped = !escaped;
					break;
				case '"':
					if (escaped) {
						builder.Append ('"');
						escaped = false;
					} else {
						quoted = !quoted;
					}
					break;
				default:
					builder.Append (text[i]);
					escaped = false;
					break;
				}
			}

			return builder.ToString ();
		}
	}
}
