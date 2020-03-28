using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Renderers;
using Markdig.Renderers.Html.Inlines;
using Markdig.Renderers.Normalize;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MarkdigAutoEmail
{
    public static class MarkdigAutoEmailLinkingExtension
    {
        public static MarkdownPipelineBuilder UseAutoEmailLinks(this MarkdownPipelineBuilder pipeline, string validPreviousCharacters = AutoEmailLinkParser.DefaultValidPreviousCharacters)
        {
            pipeline.Extensions.ReplaceOrAdd<AutoEmailLinkExtension>(new AutoEmailLinkExtension(validPreviousCharacters));
            return pipeline;
        }
    }

    public class AutoEmailLinkExtension : IMarkdownExtension
    {
        public readonly string ValidPreviousCharacters;

        public AutoEmailLinkExtension(string validPreviousCharacters = AutoEmailLinkParser.DefaultValidPreviousCharacters)
        {
            ValidPreviousCharacters = validPreviousCharacters;
        }

        public void Setup(MarkdownPipelineBuilder pipeline)
        {
            if (!pipeline.InlineParsers.Contains<AutoEmailLinkParser>())
            {
                // Insert the parser before any other parsers
                pipeline.InlineParsers.Insert(0, new AutoEmailLinkParser(ValidPreviousCharacters));
            }
        }

        public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
        {
            if (renderer is NormalizeRenderer normalizeRenderer && !normalizeRenderer.ObjectRenderers.Contains<NormalizeAutoEmailLinkRenderer>())
            {
                normalizeRenderer.ObjectRenderers.InsertBefore<LinkInlineRenderer>(new NormalizeAutoEmailLinkRenderer());
            }
        }
    }

    public class AutoEmailLinkParser : InlineParser
    {
        public AutoEmailLinkParser(string validPreviousCharacters = DefaultValidPreviousCharacters)
        {
            ValidPreviousCharacters = validPreviousCharacters;

            // Ugly / Can't work with if email starts with any word char unicode
            var openingChars = new List<char>();
            for (int i = 'a'; i <= 'z'; i++)
                openingChars.Add((char)i);
            for (int i = 'A'; i <= 'Z'; i++)
                openingChars.Add((char)i);
            for (int i = '0'; i <= '9'; i++)
                openingChars.Add((char)i);
            openingChars.Add('<');

            OpeningCharacters = openingChars.ToArray();

            _listOfCharCache = new ListOfCharCache();
        }

        // All such recognized autolinks can only come at the beginning of a line, after whitespace, or any of the delimiting characters *, _, ~, and (.
        public readonly string ValidPreviousCharacters;
        public const string DefaultValidPreviousCharacters = "*_~(";

        private static readonly Regex AutoEmailBare = new Regex(@"^<?(?:mailto:)?([-.\w]+\@[-a-z0-9]+(\.[-a-z0-9]+)*\.[a-z]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private readonly ListOfCharCache _listOfCharCache;
        public override bool Match(InlineProcessor processor, ref StringSlice slice)
        {
            // Previous char must be a whitespace or a punctuation
            var previousChar = slice.PeekCharExtra(-1);
            if (!previousChar.IsWhiteSpaceOrZero() && ValidPreviousCharacters.IndexOf(previousChar) == -1)
            {
                return false;
            }

            var pendingEmphasis = _listOfCharCache.Get();
            try
            {
                // Check that an autolink is possible in the current context
                if (!IsAutoLinkValidInCurrentContext(processor, pendingEmphasis))
                {
                    return false;
                }

                var startPosition = slice.Start;
                // Parse URL
                var match = AutoEmailBare.Match(slice.Text, slice.Start, slice.Length);
                if (!match.Success)
                {
                    return false;
                }

                // Remove <...>
                if (slice.CurrentChar == '<')
                {
                    if (slice[match.Length] != '>')
                    {
                        return false;
                    }
                    // Remove trailing >
                    slice.Start++;
                }
                
                var email = match.Groups[1].Value;
                slice.Start += match.Length;
                
                var inline = new LinkInline()
                {
                    Span =
                    {
                        Start = processor.GetSourcePosition(startPosition, out var line, out var column),
                    },
                    Line = line,
                    Column = column,
                    Url = "mailto:" + email,
                    IsClosed = true,
                    IsAutoLink = true,
                };

                inline.Span.End = inline.Span.Start + email.Length - 1;
                inline.UrlSpan = inline.Span;
                inline.AppendChild(new LiteralInline()
                {
                    Span = inline.Span,
                    Line = line,
                    Column = column,
                    Content = new StringSlice(email),
                    IsClosed = true
                });
                processor.Inline = inline;

                return true;
            }
            finally
            {
                _listOfCharCache.Release(pendingEmphasis);
            }
        }

        private bool IsAutoLinkValidInCurrentContext(InlineProcessor processor, List<char> pendingEmphasis)
        {
            // Case where there is a pending HtmlInline <a>
            var currentInline = processor.Inline;
            while (currentInline != null)
            {
                var htmlInline = currentInline as HtmlInline;
                if (htmlInline != null)
                {
                    // If we have a </a> we don't expect nested <a>
                    if (htmlInline.Tag.StartsWith("</a", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }

                    // If there is a pending <a>, we can't allow a link
                    if (htmlInline.Tag.StartsWith("<a", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                // Check previous sibling and parents in the tree
                currentInline = currentInline.PreviousSibling ?? currentInline.Parent;
            }

            // Check that we don't have any pending brackets opened (where we could have a possible markdown link)
            // NOTE: This assume that [ and ] are used for links, otherwise autolink will not work properly
            currentInline = processor.Inline;
            int countBrackets = 0;
            while (currentInline != null)
            {
                var linkDelimiterInline = currentInline as LinkDelimiterInline;
                if (linkDelimiterInline != null && linkDelimiterInline.IsActive)
                {
                    if (linkDelimiterInline.Type == DelimiterType.Open)
                    {
                        countBrackets++;
                    }
                    else if (linkDelimiterInline.Type == DelimiterType.Close)
                    {
                        countBrackets--;
                    }
                }
                else
                {
                    // Record all pending characters for emphasis
                    if (currentInline is EmphasisDelimiterInline emphasisDelimiter)
                    {
                        if (pendingEmphasis == null)
                        {
                            // Not optimized for GC, but we don't expect this case much
                            pendingEmphasis = new List<char>();
                        }
                        if (!pendingEmphasis.Contains(emphasisDelimiter.DelimiterChar))
                        {
                            pendingEmphasis.Add(emphasisDelimiter.DelimiterChar);
                        }
                    }
                }
                currentInline = currentInline.Parent;
            }

            return countBrackets <= 0;
        }

        private sealed class ListOfCharCache : DefaultObjectCache<List<char>>
        {
            protected override void Reset(List<char> instance)
            {
                instance.Clear();
            }
        }
    }

    public class NormalizeAutoEmailLinkRenderer : NormalizeObjectRenderer<LinkInline>
    {
        public override bool Accept(RendererBase renderer, MarkdownObject obj)
        {
            if (base.Accept(renderer, obj))
            {
                var normalizeRenderer = renderer as NormalizeRenderer;
                var link = obj as LinkInline;

                return normalizeRenderer != null && link != null && !normalizeRenderer.Options.ExpandAutoLinks && link.IsAutoLink;
            }
            else
            {
                return false;
            }
        }
        protected override void Write(NormalizeRenderer renderer, LinkInline obj)
        {
            renderer.Write(obj.Url);
        }
    }
}