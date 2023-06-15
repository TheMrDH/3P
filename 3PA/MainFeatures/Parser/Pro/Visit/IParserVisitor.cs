﻿#region header
// ========================================================================
// Copyright (c) 2018 - Julien Caillon (julien.caillon@gmail.com)
// This file (IParserVisitor.cs) is part of 3P.
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

namespace _3PA.MainFeatures.Parser.Pro.Visit {
    internal interface IParserVisitor {
        void PreVisit(Parse.Parser parser);
        void Visit(ParsedWord pars);
        void Visit(ParsedFile pars);
        void Visit(ParsedScopePreProcBlock pars);
        void Visit(ParsedImplementation pars);
        void Visit(ParsedPrototype pars);
        void Visit(ParsedProcedure pars);
        void Visit(ParsedIncludeFile pars);
        void Visit(ParsedPreProcVariable pars);
        void Visit(ParsedDefine pars);
        void Visit(ParsedBuffer pars);
        void Visit(ParsedTable pars);
        void Visit(ParsedOnStatement pars);
        void Visit(ParsedRun pars);
        void Visit(ParsedLabel pars);
        void Visit(ParsedFunctionCall pars);
        void Visit(ParsedFoundTableUse pars);
        void Visit(ParsedEvent pars);
        void PostVisit();
        void Visit(ParsedUsedPreProcVariable pars);
        void Visit(ParsedSnippet parsedSnippet);
    }
}