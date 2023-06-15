﻿#region header

// ========================================================================
// Copyright (c) 2018 - Julien Caillon (julien.caillon@gmail.com)
// This file (Parser.cs) is part of 3P.
//
// 3P is a free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// 3P is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with 3P. If not, see <http://www.gnu.org/licenses/>.
// ========================================================================

#endregion

using _3PA.Lib;
using _3PA.MainFeatures.AutoCompletionFeature;
using _3PA.MainFeatures.Parser.Pro.Tokenize;
using _3PA.MainFeatures.Parser.Pro.Visit;
using _3PA.NppCore;
using System;
using System.Collections.Generic;
using System.Text;
using YamuiFramework.Helper;

namespace _3PA.MainFeatures.Parser.Pro.Parse
{
    /// <summary>
    /// This class is not actually a parser "per say" but it extracts important information
    /// from the tokens created by the proLexer
    /// </summary>
    internal partial class Parser
    {
        #region static

        /// <summary>
        /// A dictionary of known keywords and database info
        /// </summary>
        private Dictionary<string, CompletionType> KnownStaticItems
        {
            get { return ParserHandler.KnownStaticItems; }
        }

        /// <summary>
        /// Set this function to return the full file path of an include (the parameter is the file name of partial path /folder/include.i)
        /// </summary>
        private Func<string, string> FindIncludeFullPath
        {
            get { return ParserHandler.FindIncludeFullPath; }
        }

        /// <summary>
        /// Instead of parsing the include files each time we store the results of the proLexer to use them when we need it
        /// </summary>
        private Dictionary<string, ProTokenizer> SavedTokenizerInclude
        {
            get { return ParserHandler.SavedTokenizerInclude; }
        }

        #endregion

        #region private fields

        /// <summary>
        /// List of the parsed items (output)
        /// </summary>
        private List<ParsedItem> _parsedItemList = new List<ParsedItem>();

        /// <summary>
        /// Contains the information of each line parsed
        /// </summary>
        private Dictionary<int, ParsedLineInfo> _lineInfo = new Dictionary<int, ParsedLineInfo>();

        /// <summary>
        /// list of errors found by the parser
        /// </summary>
        private List<ParserError> _parserErrors = new List<ParserError>();

        private List<ParsedStatement> _parsedStatementList = new List<ParsedStatement>();

        /// <summary>
        /// Path to the file being parsed (is added to the parseItem info)
        /// </summary>
        private string _filePathBeingParsed;

        private bool _lastTokenWasSpace;

        private bool _matchKnownWords;

        /// <summary>
        /// Result of the proLexer, list of tokens
        /// </summary>
        private GapBuffer<Token> _tokenList;

        private int _tokenCount;
        private int _tokenPos = -1;

        /// <summary>
        /// Contains the current information of the statement's context (in which proc it is, which scope...)
        /// </summary>
        private ParseContext _context;

        /// <summary>
        /// Contains all the words parsed
        /// </summary>
        private Dictionary<string, CompletionType> _knownWords = new Dictionary<string, CompletionType>(StringComparer.CurrentCultureIgnoreCase);

        /// <summary>
        /// Useful to remember where the function prototype was defined (Point is line, column)
        /// </summary>
        private Dictionary<string, ParsedFunction> _functionPrototype = new Dictionary<string, ParsedFunction>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// In the file being parsed we can have includes, the files included are read, tokenized, and the tokens
        /// are inserted for the current file
        /// But we need to know from which file each token is extracted, this is the purpose of this list :
        /// the [0] will designate the current procedure file, [1] the first include and so on...
        /// </summary>
        private List<ParsedIncludeFile> _parsedIncludes = new List<ParsedIncludeFile>();

        #endregion

        #region Public properties

        /// <summary>
        /// dictionary of *line, line info*
        /// </summary>
        public Dictionary<int, ParsedLineInfo> LineInfo
        {
            get { return _lineInfo; }
        }

        /// <summary>
        /// Returns the list of errors found by the parser
        /// </summary>
        public List<ParserError> ParserErrors
        {
            get { return _parserErrors; }
        }

        /// <summary>
        /// returns the list of the parsed items
        /// </summary>
        public List<ParsedItem> ParsedItemsList
        {
            get { return _parsedItemList; }
        }

        /// <summary>
        /// Returns a string that describes the errors found by the parser (relative to block start/end)
        /// Returns null if no errors were found
        /// </summary>
        public string ParseErrorsInHtml
        {
            get
            {
                var error = new StringBuilder();
                if (_parserErrors != null && _parserErrors.Count > 0)
                {
                    foreach (var parserError in _parserErrors)
                    {
                        error.AppendLine("<div>");
                        error.AppendLine(" - " + (parserError.FullFilePath + "|" + parserError.TriggerLine).ToHtmlLink("Line " + (parserError.TriggerLine + 1)) + ", " + parserError.Type.GetDescription());
                        error.AppendLine("</div>");
                    }
                }
                return error.ToString();
            }
        }

        /// <summary>
        /// Path to the file being parsed (is added to the parseItem info)
        /// </summary>
        public string FilePathBeingParsed
        {
            get { return _filePathBeingParsed; }
        }

        #endregion

        #region Life and death

        public Parser()
        { }

        /// <summary>
        /// Constructor with a string instead of a proLexer
        /// </summary>
        public Parser(string data, string filePathBeingParsed, ParsedScopeBlock defaultScope, bool matchKnownWords) : this(new ProTokenizer(data), filePathBeingParsed, defaultScope, matchKnownWords, null) { }

        public Parser(ProTokenizer proTokenizer, string filePathBeingParsed, ParsedScopeBlock defaultScope, bool matchKnownWords, StringBuilder debugListOut) : this(proTokenizer.GetTokensList, filePathBeingParsed, defaultScope, matchKnownWords, debugListOut)
        {
        }

        /// <summary>
        /// Parses a list of tokens into a list of parsedItems
        /// </summary>
        public Parser(GapBuffer<Token> tokens, string filePathBeingParsed, ParsedScopeBlock defaultScope, bool matchKnownWords, StringBuilder debugListOut)
        {
            // process inputs
            _filePathBeingParsed = filePathBeingParsed;
            _matchKnownWords = matchKnownWords && KnownStaticItems != null;

            var rootToken = new TokenEos(null, 0, 0, 0, 0) { OwnerNumber = 0 };

            // the first of this list represents the file currently being parsed
            _parsedIncludes.Add(
                new ParsedIncludeFile(
                    "root",
                    rootToken,
                    null,
                    _filePathBeingParsed,
                    null)
            );

            // init context
            _context = new ParseContext
            {
                BlockStack = new Stack<ParsedScope>(),
                CurrentStatement = new ParsedStatement(rootToken),
                CurrentStatementIsEnded = true
            };

            // create root item
            var rootScope = defaultScope ?? new ParsedFile("Root", rootToken);
            _context.BlockStack.Push(rootScope);
            if (defaultScope == null)
            {
                AddParsedItem(rootScope, 0);
            }

            // Analyze
            _tokenList = tokens;
            _tokenCount = _tokenList.Count;
            _tokenPos = -1;
            ReplaceIncludeAndPreprocVariablesAhead(1); // replaces an include or a preproc var {&x} at token position 0
            ReplaceIncludeAndPreprocVariablesAhead(2); // @position 1
            while (true)
            {
                // move to the next token
                if (++_tokenPos >= _tokenCount)
                    break;

                //  analyze the current token
                AnalyseForEachToken(PeekAt(0));

                try
                {
                    Analyze();
                }
                catch (Exception e)
                {
                    ErrorHandler.LogError(e, "Error while parsing the following file : " + filePathBeingParsed);
                }
            }
            AddLineInfo(_tokenList[_tokenList.Count - 1]); // add info on last line
            PopOneStatementIndentBlock(0); // make sure to pop the final block

            // add missing values to the line dictionary
            // missing values will be for the lines within a multilines comment/string for which we didn't match an EOL to add line info
            var currentLineInfo = _lineInfo[_tokenList[_tokenList.Count - 1].Line];
            for (int i = PeekAt(-1).Line - 1; i >= 0; i--)
            {
                if (!_lineInfo.ContainsKey(i))
                    _lineInfo.Add(i, currentLineInfo);
                else
                    currentLineInfo = _lineInfo[i];
            }

            // check for parser errors
            while (_context.BlockStack.Count > 1)
            {
                ParsedScope scope = _context.BlockStack.Pop();
                // check that we match a RESUME for each SUSPEND
                if (scope is ParsedScopePreProcBlock)
                    _parserErrors.Add(new ParserError(ParserErrorType.MissingUibBlockEnd, PeekAt(-1), _context.BlockStack.Count, _parsedIncludes));

                // check that we match an &ENDIF for each &IF
                else if (scope is ParsedScopePreProcIfBlock)
                    _parserErrors.Add(new ParserError(ParserErrorType.MissingPreprocEndIf, PeekAt(-1), _context.BlockStack.Count, _parsedIncludes));

                // check that we match an END. for each block
                else
                    _parserErrors.Add(new ParserError(ParserErrorType.MissingBlockEnd, PeekAt(-1), _context.BlockStack.Count, _parsedIncludes));
            }

            // returns the concatenation of all the tokens once the parsing is done
            if (debugListOut != null)
            {
                foreach (var token in _tokenList)
                {
                    debugListOut.Append(token.Value);
                }
            }

            // dispose
            _context.BlockStack = null;
            _context = null;
            _tokenList = null;
            _functionPrototype = null;
            _parsedIncludes = null;
            _knownWords = null;

            // if we are parsing an include file that was saved for later use, update it
            if (SavedTokenizerInclude.ContainsKey(filePathBeingParsed))
                SavedTokenizerInclude.Remove(filePathBeingParsed);
        }

        #endregion

        #region Visitor implementation

        /// <summary>
        /// Feed this method with a visitor implementing IParserVisitor to visit all the parsed items
        /// </summary>
        /// <param name="visitor"></param>
        public void Accept(IParserVisitor visitor)
        {
            visitor.PreVisit(this);
            foreach (var item in _parsedItemList)
            {
                item.Accept(visitor);
            }
            visitor.PostVisit();
        }

        #endregion

        #region Explore tokens list

        /// <summary>
        /// Peek forward x tokens, returns an TokenEof if out of limits (can be used with negative values)
        /// </summary>
        private Token PeekAt(int x)
        {
            return (_tokenPos + x >= _tokenCount || _tokenPos + x < 0) ? new TokenEof("", -1, -1, -1, -1) : _tokenList[_tokenPos + x];
        }

        /// <summary>
        /// Peek forward (or backward if goBackWard = true) until we match a token that is not a space token
        /// return found token
        /// </summary>
        private Token PeekAtNextType<T>(int start, bool goBackward = false) where T : Token
        {
            int x = start + (goBackward ? -1 : 1);
            var tok = PeekAt(x);
            while (!(tok is T) && !(tok is TokenEof))
                tok = PeekAt(goBackward ? x-- : x++);
            return tok;
        }

        /// <summary>
        /// Peek forward (or backward if goBackWard = true) until we match a token that is not a space token
        /// return found token
        /// </summary>
        private Token PeekAtNextNonType<T>(int start, bool goBackward = false) where T : Token
        {
            int x = start + (goBackward ? -1 : 1);
            var tok = PeekAt(x);
            while (tok is T && !(tok is TokenEof))
                tok = PeekAt(goBackward ? x-- : x++);
            return tok;
        }

        /// <summary>
        /// Move to the next token
        /// </summary>
        private bool MoveNext()
        {
            // move to the next token
            if (++_tokenPos >= _tokenCount)
                return false;

            //  analyze the current token
            AnalyseForEachToken(PeekAt(0));

            return true;
        }

        /// <summary>
        /// Replace the token at the current pos + x by the token given
        /// </summary>
        private void ReplaceToken(int x, Token token)
        {
            if (_tokenPos + x < _tokenCount)
                _tokenList[_tokenPos + x] = token;
        }

        /// <summary>
        /// Inserts tokens at the current pos + x
        /// </summary>
        private void InsertTokens(int x, List<Token> tokens)
        {
            if (_tokenPos + x < _tokenCount)
            {
                _tokenList.InsertRange(_tokenPos + x, tokens);
                _tokenCount = _tokenList.Count;
            }
        }

        private void RemoveTokens(int x, int count)
        {
            count = count.ClampMax(_tokenCount - _tokenPos - x);
            if (_tokenPos + x + count <= _tokenCount && count > 0)
            {
                _tokenList.RemoveRange(_tokenPos + x, count);
                _tokenCount = _tokenList.Count;
            }
        }

        #endregion

        #region internal classes

        /// <summary>
        /// contains the info on the current context (as we move through tokens)
        /// </summary>
        private class ParseContext
        {
            /// <summary>
            /// Keep tracks on blocks through a stack (a block == an indent)
            /// </summary>
            public Stack<ParsedScope> BlockStack { get; set; }

            public ParsedStatement CurrentStatement { get; set; }

            /// <summary>
            /// Allows to read the next word after a THEN or a ELSE as the first word of a statement to
            /// correctly read ASSIGN statement for instance...
            /// </summary>
            public bool ReadNextWordAsStatementStart { get; set; }

            /// <summary>
            /// Last ON block matched
            /// </summary>
            public ParsedScopeBlock LastOnBlock { get; set; }

            public ParsedLabel LastLabel { get; set; }

            /// <summary>
            /// Useful to know if, during the last &amp;if / elseif / else / endif,
            /// one of the if expression hsa been true. If not, we know that the &amp;else block will be true
            /// </summary>
            public bool LastPreprocIfwasTrue { get; set; }

            public bool InFalsePreProcIfBlock { get; set; }

            public bool CurrentStatementIsEnded { get; set; }
        }

        private class ParsedStatement
        {
            /// <summary>
            /// A statement can start with a word, pre-proc phrase or an include
            /// </summary>
            public Token FirstToken { get; private set; }

            /// <summary>
            /// True if the first word of the statement didn't match a known statement
            /// </summary>
            public bool UnknownFirstWord { get; set; }

            /// <summary>
            /// Number of words count in the current statement
            /// </summary>
            public int WordCount { get; set; }

            public ParsedStatement(Token firstToken)
            {
                FirstToken = firstToken;
            }
        }

        #endregion
    }
}