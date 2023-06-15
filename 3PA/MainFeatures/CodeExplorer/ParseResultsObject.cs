using _3PA.MainFeatures.AutoCompletionFeature;
using _3PA.MainFeatures.Parser.Pro;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace _3PA.MainFeatures.CodeExplorer
{
    public class ParseResultsObject
    {
        string hash { get; set; } = "";
        List<CompletionItem> CompletionItemsList { get; set; } = new List<CompletionItem>();
        List<CodeItem> ExplorerItemsList { get; set; } = new List<CodeItem>();
        List<ParserError> ParserErrors { get; set; } = new List<ParserError>();
        Dictionary<int, ParsedLineInfo> ParsedLineInfo { get; set; }
        List<ParsedItem> ParsedItems { get; set; } = new List<ParsedItem>();

    }
}
