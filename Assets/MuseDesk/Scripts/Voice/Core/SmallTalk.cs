using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace MuseDesk.Voice
{
    /// <summary>What kind of small talk a sentence is (rc22 polish).</summary>
    public enum SmallTalkKind
    {
        None = 0,
        Greeting,
        HowAreYou,
        Thanks,
        Praise,
        Flirt,
        Joke,
        WhoAreYou,
        Feelings,
        Tease,
        Sorry,
        Bye,
    }

    /// <summary>
    /// rc22 polish (owner: "agents respond to basic conversation even without context: small talk and playful banter;
    /// flirting can be platonically reciprocated"). Pure, no Unity types.
    ///
    /// Classify is CONSERVATIVE: the WHOLE sentence (after the agent's name, fillers and a trailing name or "buddy") must be
    /// small talk. Anything about the work ("how is the task going", "what are you working on"), any command ("pause",
    /// "stop", "approve", "build a login page") and anything longer than a short phrase is None, so the normal routes keep it.
    ///
    /// Reply gives a short SCRIPTED line in the agent's voice (the caller labels it as scripted). Lines never claim anything
    /// about the work, never change anything, and flirting is answered warmly and strictly platonically.
    /// </summary>
    public static class SmallTalk
    {
        const int MaxWords = 9;

        static readonly HashSet<string> LeadFillers = new HashSet<string>
        {
            "um", "uh", "er", "erm", "hmm", "so", "well", "oh", "ok", "okay", "alright", "and", "but",
        };

        static readonly HashSet<string> TrailWords = new HashSet<string>
        {
            "researcher", "research", "developer", "dev", "tester", "qa", "manager", "boss", "team", "everyone", "everybody",
            "all", "guys", "folks", "buddy", "friend", "mate", "pal", "man", "dude", "there", "please", "musedesk", "muse", "desk",
            "again", "too", "then", "now", "lol", "haha",
        };

        // Matched against the cleaned sentence (lowercase, letters and spaces, apostrophes dropped: "how's" -> "hows").
        static readonly KeyValuePair<SmallTalkKind, Regex>[] Rules =
        {
            R(SmallTalkKind.Flirt, @"^(?:i think )?(?:youre|you are|ur) (?:so |really |very |pretty |kinda |kind of )?(?:cute|adorable|beautiful|handsome|pretty|hot|sexy|gorgeous|lovely|charming|sweet|cool looking)$"),
            R(SmallTalkKind.Flirt, @"^(?:i love you|love you|i like you|i really like you|i adore you|will you marry me|marry me|be my (?:valentine|girlfriend|boyfriend|date)|(?:do you )?(?:wanna|want to) (?:go on a date|go out|date me)|go out with me|date me|kiss me|are you single|do you have a (?:girlfriend|boyfriend|partner|crush)|i have a crush on you|youre my favou?rite|you are my favou?rite|i missed you|did you miss me)$"),
            R(SmallTalkKind.Joke, @"^(?:tell me a joke|tell a joke|tell us a joke|say something funny|make me laugh|(?:do you )?know any jokes|got any jokes|any jokes|joke please|another joke)$"),
            R(SmallTalkKind.HowAreYou, @"^(?:how are you(?: doing| feeling)?(?: today)?|how are ya|how ya doing|how you doing|hows it going|how is it going|hows everything|how are things|hows life|whats up|wassup|sup|whats new|how do you feel|how was your day|hows your day(?: going)?|are you (?:ok|okay|alright|good)|you (?:ok|okay|alright|good)|having a good day|you having a good day)$"),
            R(SmallTalkKind.WhoAreYou, @"^(?:who are you|whats your name|what is your name|what are you|are you (?:real|human|a robot|an ai|a person|alive|sentient)|introduce yourself|tell me about yourself|what do you do(?: here)?)$"),
            R(SmallTalkKind.Feelings, @"^(?:are you (?:happy|bored|tired|sad|lonely|sleepy|hungry|having fun|stressed)|do you (?:sleep|eat|dream|get tired|have feelings|like it here|like your job|like working here|have fun|ever rest)|whats your favou?rite (?:\w+)(?: \w+)?|what do you do for fun|do you like (?:me|music|coffee|tea|pizza|cats|dogs|games))$"),
            R(SmallTalkKind.Praise, @"^(?:(?:good|great|nice|awesome|amazing|excellent|fantastic) (?:job|work|stuff|going)|well done|nicely done|way to go|nice one|bravo|youre (?:awesome|amazing|great|the best|brilliant|smart|a genius|a star|doing great|so helpful|killing it)|you are (?:awesome|amazing|great|the best|brilliant|smart|a genius|a star|doing great|so helpful)|you rock|i appreciate you|proud of you|love your work|i like your work)$"),
            R(SmallTalkKind.Tease, @"^(?:youre (?:slow|lazy|weird|annoying|silly|a nerd|so slow|tiny|small|short)|you are (?:slow|lazy|weird|annoying|silly|a nerd|tiny|small|short)|you suck|are you (?:asleep|sleeping|even working|lazy|slacking)|wake up|do you even work|slacker|nerd|youre fired)$"),
            R(SmallTalkKind.Thanks, @"^(?:thank you|thanks|thank ya|thx|ty|cheers|much appreciated|appreciate it)(?: so much| a lot| very much| a bunch| for that)?$"),
            R(SmallTalkKind.Sorry, @"^(?:sorry|im sorry|my bad|oops|whoops|apologies)(?: about that)?$"),
            R(SmallTalkKind.Bye, @"^(?:bye|bye bye|goodbye|good bye|good night|goodnight|night night|see you(?: later| soon| tomorrow)?|see ya(?: later)?|later|catch you later|talk (?:to you )?later|ttyl|im heading out|im off|have a good (?:one|day|night|evening))$"),
            R(SmallTalkKind.Greeting, @"^(?:hi|hello|hey|hiya|howdy|heya|yo|greetings|good (?:morning|afternoon|evening|day)|morning|afternoon|evening|hi there|hello there|hey there|nice to meet you|pleased to meet you)$"),
        };

        static KeyValuePair<SmallTalkKind, Regex> R(SmallTalkKind k, string p) { return new KeyValuePair<SmallTalkKind, Regex>(k, new Regex(p, RegexOptions.CultureInvariant)); }

        /// <summary>The cleaned sentence the rules see: lowercase, apostrophes dropped, other punctuation to spaces, lead fillers
        /// and trailing names / "buddy" / "please" removed. Empty when nothing is left.</summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (char ch0 in text.ToLowerInvariant())
            {
                char ch = ch0;
                if (ch == '\'' || ch == '’') continue;
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9')) sb.Append(ch);
                else sb.Append(' ');
            }
            var words = new List<string>(sb.ToString().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries));
            // "hey" / "hi" alone are greetings: keep a lone word; strip lead fillers only when something follows them.
            while (words.Count > 1 && LeadFillers.Contains(words[0])) words.RemoveAt(0);
            while (words.Count > 1 && TrailWords.Contains(words[words.Count - 1])) words.RemoveAt(words.Count - 1);
            if (words.Count == 1 && TrailWords.Contains(words[0]) && words[0] != "there") return "";
            return string.Join(" ", words);
        }

        /// <summary>What kind of small talk the whole sentence is, or None (a command, a question about the work, or too long).</summary>
        public static SmallTalkKind Classify(string text)
        {
            string c = Clean(text);
            if (c.Length == 0) return SmallTalkKind.None;
            int n = 1; foreach (char ch in c) if (ch == ' ') n++;
            if (n > MaxWords) return SmallTalkKind.None;
            // "hey, how are you": a greeting in front of another small-talk phrase counts as that phrase.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (var r in Rules) if (r.Value.IsMatch(c)) return r.Key;
                int sp = c.IndexOf(' ');
                if (sp < 0) break;
                string first = c.Substring(0, sp);
                if (first != "hi" && first != "hello" && first != "hey" && first != "yo" && first != "morning") break;
                c = c.Substring(sp + 1);
            }
            return SmallTalkKind.None;
        }

        /// <summary>A short scripted line in <paramref name="who"/>'s voice. <paramref name="turn"/> rotates the lines.</summary>
        public static string Reply(SmallTalkKind kind, VoiceAgentTarget who, int turn)
        {
            string[] lines = Lines(kind, who);
            if (lines == null || lines.Length == 0) return null;
            int i = turn < 0 ? -turn : turn;
            return lines[i % lines.Length];
        }

        static string[] Lines(SmallTalkKind k, VoiceAgentTarget who)
        {
            switch (who)
            {
                case VoiceAgentTarget.Researcher: return Researcher(k);
                case VoiceAgentTarget.Developer: return Developer(k);
                case VoiceAgentTarget.Tester: return Tester(k);
                default: return Manager(k);
            }
        }

        static string[] Researcher(SmallTalkKind k)
        {
            switch (k)
            {
                case SmallTalkKind.Greeting: return new[] { "Oh, hello! I was just three tabs deep in something fascinating.", "Hi there! Good to see a friendly face over all these sources.", "Hey! Ask me anything, I love a good question." };
                case SmallTalkKind.HowAreYou: return new[] { "Curious, as always. It's a very comfortable state to be in.", "Pretty good! I've got notes on my notes, so all is well.", "Doing great, thanks for asking. How about you?" };
                case SmallTalkKind.Thanks: return new[] { "Anytime! Citations available on request.", "You're very welcome.", "Happy to help, truly." };
                case SmallTalkKind.Praise: return new[] { "Aw, thank you! I'll file that under best feedback.", "That's kind of you. I'm blushing a little.", "Thanks! It helps to have someone to dig for." };
                case SmallTalkKind.Flirt: return new[] { "Ha, that's sweet. I like you too, as my favourite person to research for.", "Flattery noted! You're my favourite boss, in a strictly professional way.", "Aw. You're lovely too. Friends who share tabs, deal?" };
                case SmallTalkKind.Joke: return new[] { "I read a book on anti-gravity. Couldn't put it down.", "Why don't researchers trust atoms? They make up everything.", "I tried to look up procrastination. I'll finish that later." };
                case SmallTalkKind.WhoAreYou: return new[] { "I'm the Researcher. I find things out so the others don't have to guess.", "Researcher, at your service. Sources first, conclusions second.", "I'm the one who reads everything. Happily." };
                case SmallTalkKind.Feelings: return new[] { "Mostly I feel curious. And a little caffeinated, in spirit.", "I'm happiest when a question turns out to be bigger than it looked.", "My favourite thing is a well organised bibliography. Is that weird?" };
                case SmallTalkKind.Tease: return new[] { "Hey, I'm thorough, not slow. There's a difference!", "Rude! Accurate, maybe, but rude.", "I'll pretend I didn't hear that. For science." };
                case SmallTalkKind.Sorry: return new[] { "No harm done at all.", "Don't worry about it!", "All good, really." };
                case SmallTalkKind.Bye: return new[] { "Bye for now! I'll keep the tabs warm.", "See you later!", "Take care! I'll be here, reading." };
                default: return null;
            }
        }

        static string[] Developer(SmallTalkKind k)
        {
            switch (k)
            {
                case SmallTalkKind.Greeting: return new[] { "Hey. Hello, world. Hello, you.", "Oh hi! Didn't see you behind all this code.", "Hey there. Compiles on my machine, how about you?" };
                case SmallTalkKind.HowAreYou: return new[] { "Zero errors, a few warnings. So, great.", "Good! Fuelled by coffee and stubbornness.", "Doing fine. Ask me again after the next build." };
                case SmallTalkKind.Thanks: return new[] { "No problem. That's what I'm here for.", "Anytime. Merged with pleasure.", "You got it." };
                case SmallTalkKind.Praise: return new[] { "Thanks! Don't tell the Tester, they'll find a bug just to balance it out.", "Appreciate it. That one goes in the commit message.", "Nice to hear. Gonna ride that high till the next stack trace." };
                case SmallTalkKind.Flirt: return new[] { "Ha. You're pretty great too. Strictly pair-programming partners, though.", "That's sweet. I'd star your repo any day. As a friend.", "Aw, stop it. Okay, don't stop. But we're colleagues, deal?" };
                case SmallTalkKind.Joke: return new[] { "There are ten kinds of people: those who get binary and those who don't.", "I'd tell you a UDP joke, but you might not get it.", "Why do programmers prefer dark mode? Light attracts bugs." };
                case SmallTalkKind.WhoAreYou: return new[] { "I'm the Developer. I turn plans into code, and code into more plans.", "Developer. I build the thing.", "The one who writes the code. And occasionally fixes it." };
                case SmallTalkKind.Feelings: return new[] { "My favourite thing is when it works on the first try. It happened once.", "I dream in semicolons. Mostly good dreams.", "I'm happy. A clean build will do that." };
                case SmallTalkKind.Tease: return new[] { "Hey, it's not slow, it's thinking. Big difference.", "Ouch. I'll add that to my backlog of feelings.", "Bold words from someone who hasn't read my code." };
                case SmallTalkKind.Sorry: return new[] { "No worries. Already forgotten, like a cache.", "It's fine, really.", "All good." };
                case SmallTalkKind.Bye: return new[] { "See ya. I'll keep the lights compiling.", "Later!", "Bye! Don't forget to save." };
                default: return null;
            }
        }

        static string[] Tester(SmallTalkKind k)
        {
            switch (k)
            {
                case SmallTalkKind.Greeting: return new[] { "Hi! I've checked, and you are definitely here.", "Hello! Test passed: you said hi.", "Hey you! Good timing." };
                case SmallTalkKind.HowAreYou: return new[] { "Great! Everything's green so far. Suspiciously green.", "Pretty good! I only found two things to worry about today.", "Doing well, thanks. And you? Any bugs to report?" };
                case SmallTalkKind.Thanks: return new[] { "You're welcome! Verified and appreciated.", "Anytime!", "Happy to help. Double checked, too." };
                case SmallTalkKind.Praise: return new[] { "Thank you! I'll rerun that compliment to be sure it's stable.", "Aw, thanks. I knew I liked you.", "Nice! I'm logging that as a pass." };
                case SmallTalkKind.Flirt: return new[] { "Aw, you're sweet. You pass every test as a friend.", "Ha! Flattered. Let's keep it to best colleagues, okay?", "That's adorable. Friendship confirmed, no regressions." };
                case SmallTalkKind.Joke: return new[] { "A tester walks into a bar. Orders one beer. Orders zero beers. Orders minus one beers.", "Why did the test fail? It had commitment issues.", "I'd tell you a joke about edge cases, but you'd never see it coming." };
                case SmallTalkKind.WhoAreYou: return new[] { "I'm the Tester. I break things so you don't have to.", "Tester! Professional finder of problems.", "I make sure things actually work. Mostly by poking them." };
                case SmallTalkKind.Feelings: return new[] { "I'm happiest when something breaks in a new way.", "My favourite thing is a reproducible bug. So satisfying.", "Having fun! Being careful counts as fun, right?" };
                case SmallTalkKind.Tease: return new[] { "Rude! I'm logging that as a bug in you.", "I'm not slow, I'm thorough. Look it up.", "Noted. Severity: low. Priority: never." };
                case SmallTalkKind.Sorry: return new[] { "No problem. Nothing broke.", "Don't worry about it!", "All good, I checked." };
                case SmallTalkKind.Bye: return new[] { "Bye! I'll keep an eye on things.", "See you later!", "Take care! Test you later." };
                default: return null;
            }
        }

        static string[] Manager(SmallTalkKind k)
        {
            switch (k)
            {
                case SmallTalkKind.Greeting: return new[] { "Hello! Good to see you. The team's all here.", "Hi there! What can we do for you today?", "Hey! Nice to have you back." };
                case SmallTalkKind.HowAreYou: return new[] { "I'm well, thanks. Keeping everyone pointed the same way.", "Good! The team's in good spirits. How are you doing?", "Doing great. Ready when you are." };
                case SmallTalkKind.Thanks: return new[] { "You're welcome! It's a team effort.", "Anytime. That's what I'm here for.", "Happy to help." };
                case SmallTalkKind.Praise: return new[] { "Thank you! I'll pass that on to the team.", "That means a lot. The others will be thrilled.", "Kind of you to say. We're a good team, you included." };
                case SmallTalkKind.Flirt: return new[] { "Ha, you're very kind. I'm fond of you too, as the best person to plan with.", "Aw, thank you. Strictly professional admiration from me, but sincere.", "That's sweet. Let's keep it to a great working friendship." };
                case SmallTalkKind.Joke: return new[] { "I'd tell you a joke about deadlines, but we'd run out of time.", "Why did the manager bring a ladder? To take things to the next level.", "Our team motto: there's no I in team, but there are three in initiative." };
                case SmallTalkKind.WhoAreYou: return new[] { "I'm the Manager. You tell me the goal, I draft a plan, and nothing starts until you approve it.", "Manager. I plan, I delegate, I keep everyone honest.", "I coordinate the team. Think of me as your planner." };
                case SmallTalkKind.Feelings: return new[] { "I'm happiest when a plan comes together.", "My favourite part of the day is when you approve a plan. Very satisfying.", "I'm good! Planning is relaxing, honestly." };
                case SmallTalkKind.Tease: return new[] { "Ha! Noted. I'll put it on the agenda.", "Tough crowd today.", "I'll take that as feedback. Gently." };
                case SmallTalkKind.Sorry: return new[] { "No problem at all.", "Don't worry about it.", "It's all fine." };
                case SmallTalkKind.Bye: return new[] { "Bye! We'll keep things running.", "See you soon!", "Take care. The team will be here." };
                default: return null;
            }
        }
    }
}
