﻿using System;
using System.Collections.Generic;
using System.Text;
using OpenUtau.Api;
using OpenUtau.Core.Ustx;
using System.Linq;
using System.IO;

namespace OpenUtau.Plugin.Builtin {
    /// <summary>
    /// Use this class as a base for easier phonemizer configuration
    /// Works for vb styles like VCV, VCCV, CVC etc, supports dictionary. 
    /// </summary>
    public abstract class AdvancedPhonemizer : Phonemizer {

        /// <summary>
        /// result of dictionary parsing. Phonemes from previous word are included to the first syllable
        /// </summary>
        protected struct Word {
            public Syllable[] syllables;
            public Ending ending;

            public override string ToString() {
                return $"{(syllables != null ? string.Join(",", syllables.Select(n => n.ToString())) : "")} {ending}";
            }
        }

        /// <summary>
        /// Syllable is [V] [C..] [V]
        /// </summary>
        protected struct Syllable {
            /// <summary>
            /// vowel from previous syllable for VC
            /// </summary>
            public string prevV;
            /// <summary>
            /// CCs, may be empty
            /// </summary>
            public string[] cc;
            /// <summary>
            /// "base" note. May not actually be vowel, if only consonants way provided
            /// </summary>
            public string v;
            /// <summary>
            /// Start position for vowel. All VC CC goes before this position
            /// </summary>
            public int position;
            /// <summary>
            /// previous note duration, i.e. this is container for VC and CC notes
            /// </summary>
            public int duration;
            /// <summary>
            /// Tone for VC and CC
            /// </summary>
            public int tone;
            /// <summary>
            /// tone for base "vowel" phoneme
            /// </summary>
            public int vowelTone;

            public override string ToString() {
                return $"({prevV}) {(cc != null ? string.Join(" ", cc) : "")} {v}";
            }
        }

        protected struct Ending {
            /// <summary>
            /// vowel from the last syllable to make VC
            /// </summary>
            public string prevV;
            /// <summary>
            ///  actuall CC at the ending
            /// </summary>
            public string[] cc;
            /// <summary>
            /// last note position + duration, all phonemes must be less than this
            /// </summary>
            public int position;
            /// <summary>
            /// last syllable length, max container for all VC CC C-
            /// </summary>
            public int duration;
            /// <summary>
            /// the tone from last syllable, for all ending phonemes
            /// </summary>
            public int tone;


            public override string ToString() {
                return $"({prevV}) {(cc != null ? string.Join(" ", cc) : "")}";
            }
        }

        public override Result Process(Note[] notes, Note? prev, Note? next, Note? prevNeighbour, Note? nextNeighbour, Note[] prevNeighbours) {
            error = "";
            var mainNote = notes[0];
            if (mainNote.lyric.StartsWith(FORCED_ALIAS_SYMBOL)) {
                return MakeForcedAliasResult(mainNote);
            }

            Word? prevWord = null;
            if (prevNeighbours.Length > 0 && !prevNeighbours[0].lyric.StartsWith(FORCED_ALIAS_SYMBOL)) {
                prevWord = MakeWord(prevNeighbours);
            }
            var tryWord = MakeWord(notes, prevWord);
            if (!tryWord.HasValue) {
                return HandleError();
            }
            var word = tryWord.Value;

            var phonemes = new List<Phoneme>();
            foreach (var syllable in word.syllables) {
                phonemes.AddRange(MakePhonemes(TrySyllable(syllable), syllable.duration, syllable.position,
                    syllable.tone, syllable.vowelTone, false));
            }
            if (!nextNeighbour.HasValue) {
                phonemes.AddRange(MakePhonemes(TryEnding(word.ending), word.ending.duration, word.ending.position, 
                    word.ending.tone, word.ending.tone, true));
            }

            return new Result() {
                phonemes = phonemes.ToArray()
            };
        }

        private Result HandleError() {
            return new Result {
                phonemes = new Phoneme[] {
                    new Phoneme() {
                        phoneme = error
                    }
                }
            };
        }

        public override void SetSinger(USinger singer) {
            this.singer = singer;
            if (!hasDictionary) {
                ReadDictionary();
            }
            Init();
        }

        protected USinger singer;
        protected bool hasDictionary => dictionaries.ContainsKey(GetType());
        protected G2pDictionary dictionary => dictionaries[GetType()];

        private static Dictionary<Type, G2pDictionary> dictionaries = new Dictionary<Type, G2pDictionary>();
        private const string FORCED_ALIAS_SYMBOL = "/";
        private string error = "";

        /// <summary>
        /// Returns list of vowels
        /// </summary>
        /// <returns></returns>
        protected abstract string[] GetVowels();

        /// <summary>
        /// Returns list of consonants. Only needed if there is a dictionary
        /// </summary>
        /// <returns></returns>
        protected virtual string[] GetConsonants() {
            throw new NotImplementedException();
        }

        /// <summary>
        /// returns phoneme symbols, like, VCV, or VC + CV, or -CV, etc
        /// </summary>
        /// <returns>List of phonemes</returns>
        protected abstract List<string> TrySyllable(Syllable syllable);

        /// <summary>
        /// phoneme symbols for ending, like, V-, or VC-, or VC+C
        /// </summary>
        protected abstract List<string> TryEnding(Ending ending);

        /// <summary>
        /// simple alias to alias fallback
        /// </summary>
        /// <returns></returns>
        protected virtual Dictionary<string, string> GetAliasesFallback() { return null; }

        /// <summary>
        /// Use to some custom init, if needed
        /// </summary>
        protected virtual void Init() { }

        /// <summary>
        /// Dictionary content. Expected to be stored in resources
        /// </summary>
        /// <returns></returns>
        protected virtual string GetDictionary() { return null; }

        /// <summary>
        /// extracts array of phoneme symbols from note. Override for procedural dictionary or something
        /// reads from dictionary if provided
        /// </summary>
        /// <param name="note"></param>
        /// <returns></returns>
        protected virtual string[] GetSymbols(Note note) {
            string[] getSymbolsRaw(string lyrics) {
                if (lyrics == null) {
                    return new string[0];
                } else return lyrics.Split(" ");
            }

            if (hasDictionary) {
                if (!string.IsNullOrEmpty(note.phoneticHint)) {
                    return getSymbolsRaw(note.phoneticHint);
                }
                var result = new List<string>();
                foreach (var subword in note.lyric.Trim().ToLowerInvariant().Split(new string[] { " ", "_" }, StringSplitOptions.RemoveEmptyEntries)) {
                    var subResult = dictionary.Query(subword);
                    if (subResult == null) {
                        error = "word not found";
                        return null;
                    }
                    result.AddRange(subResult);
                }
                return result.ToArray();
            }
            else {
                return getSymbolsRaw(note.lyric);
            }
        }

        /// <summary>
        /// Checks if mapped and validated alias exists in oto
        /// </summary>
        /// <param name="alias"></param>
        /// <param name="note"></param>
        /// <returns></returns>
        protected bool HasOto(string alias, int tone) {
            return singer.TryGetMappedOto(ValidateAlias(alias), tone, out _);
        }

        /// <summary>
        /// Instead of changing symbols in cmudict itself for each reclist, 
        /// you may leave it be and provide symbol replacements with this method.
        /// </summary>
        /// <returns></returns>
        protected virtual Dictionary<string, string> GetDictionaryPhonemesReplacement() { return null; }

        /// <summary>
        /// separates symbols to syllables and ending.
        /// Not sure you would like to override it, but, anyway.
        /// </summary>
        /// <param name="notes"></param>
        /// <param name="prevWord"></param>
        /// <returns></returns>
        protected virtual Word? MakeWord(Note[] notes, Word? prevWord = null) {
            var mainNote = notes[0];
            var symbols = GetSymbols(mainNote);
            if (symbols == null) {
                return null;
            }
            if (symbols.Length == 0) {
                symbols = new string[] { "" };
            }
            symbols = ApplyExtensions(symbols, notes);
            List<int> vowelIds = ExtractVowels(symbols);
            if (vowelIds.Count == 0) {
                // no syllables or all consonants, the last phoneme will be interpreted as vowel
                vowelIds.Add(symbols.Length - 1);
            }
            var firstVowelId = vowelIds[0];
            if (notes.Length < vowelIds.Count) {
                error = $"Not enough extension notes, {vowelIds.Count - notes.Length} more expected";
                return null;
            }
            Word word = new Word() {
                syllables = new Syllable[vowelIds.Count]
            };

            // Making the first syllable
            if (prevWord.HasValue) {
                var prevEnding = prevWord.Value.ending;
                // prev word comes in one note, so all syllables except the first one are in ending
                // so we need to separate the vowels manually
                var prevEndingVowels = ExtractVowels(prevEnding.cc);
                var beginningCc = prevEndingVowels.Count == 0 ? prevEnding.cc.ToList() : prevEnding.cc.Skip(prevEndingVowels.Last()).ToList();
                beginningCc.AddRange(symbols.Take(firstVowelId));

                // If we had a prev neighbour, let's take info from it
                word.syllables[0] = new Syllable() {
                    prevV = prevEnding.prevV,
                    cc = beginningCc.ToArray(),
                    v = symbols[firstVowelId],
                    tone = prevEnding.tone,
                    duration = prevEnding.duration,
                    position = 0,
                    vowelTone = mainNote.tone
                };
            } else {
                // there is only empty space before us
                word.syllables[0] = new Syllable() {
                    prevV = "",
                    cc = symbols.Take(firstVowelId).ToArray(),
                    v = symbols[firstVowelId],
                    tone = mainNote.tone,
                    duration = -1,
                    position = 0,
                    vowelTone = mainNote.tone
                };
            }

            // normal syllables after the first one
            var syllableI = 1;
            var noteI = 1;
            var ccs = new List<string>();
            var position = 0;
            var lastVowelI = firstVowelId + 1;
            for (; lastVowelI < symbols.Length & syllableI < notes.Length; lastVowelI++) {
                if (!vowelIds.Contains(lastVowelI)) {
                    ccs.Add(symbols[lastVowelI]);
                } else {
                    position += notes[syllableI - 1].duration;
                    word.syllables[syllableI] = new Syllable() {
                        prevV = word.syllables[syllableI - 1].v,
                        cc = ccs.ToArray(),
                        v = symbols[lastVowelI],
                        tone = word.syllables[syllableI - 1].vowelTone,
                        duration = notes[noteI - 1].duration,
                        position = position,
                        vowelTone = notes[noteI].tone
                    };
                    ccs = new List<string>();
                    syllableI++;
                }
            }

            position += notes.Skip(vowelIds.Count).Sum(n => n.duration);

            // making the ending
            var lastNote = notes.Last();
            word.ending = new Ending() {
                prevV = word.syllables[syllableI - 1].v,
                cc = symbols.Skip(lastVowelI).ToArray(),
                tone = lastNote.tone,
                duration = lastNote.duration,
                position = position + lastNote.duration
            };

            return word;
        }

        /// <summary>
        /// Does this note extend the previous syllable?
        /// </summary>
        /// <param name="note"></param>
        /// <returns></returns>
        protected bool IsSyllableExtensionNote(Note note) {
            return note.lyric.StartsWith("...~") || note.lyric.StartsWith("...*");
        }

        /// <summary>
        /// May be used if you have different logic for short and long notes
        /// </summary>
        /// <param name="syllable"></param>
        /// <returns></returns>
        protected bool IsShort(Syllable syllable) {
            return syllable.duration != -1 && TickToMs(syllable.duration) < GetTransitionBasicLength() * 2;
        }
        protected bool IsShort(Ending ending) {
            return TickToMs(ending.duration) < GetTransitionBasicLength() * 2;
        }

        /// <summary>
        /// Used to extract phonemes from CMU Dict word. Override if you need some extra logic
        /// </summary>
        /// <param name="phonemesString"></param>
        /// <returns></returns>
        protected virtual string[] GetDictionaryWordPhonemes(string phonemesString) {
            return phonemesString.Split(' ');
        }

        /// <summary>
        /// use to validate alias
        /// </summary>
        /// <param name="alias"></param>
        /// <returns></returns>
        protected virtual string ValidateAlias(string alias) {
            return alias;
        }

        /// <summary>
        /// multiply if some transitions are supposed to be longer or shorter
        /// </summary>
        /// <returns></returns>
        protected virtual double GetTransitionBasicLength(string alias = "") {
            return this.bpm < 190 ? 105 : this.bpm < 250 ? 90 : this.bpm < 300 ? 75 : 60;
        }

        #region private

        private Result MakeForcedAliasResult(Note note) {
            return new Result {
                phonemes = new Phoneme[] {
                    new Phoneme {
                        phoneme = note.lyric.Substring(1)
                    }
                }
            };
        }

        private void ReadDictionary() {
            var dictionary = GetDictionary();
            if (dictionary == null)
                return;
            try {
                var builder = G2pDictionary.NewBuilder();
                var vowels = GetVowels();
                foreach (var vowel in vowels) {
                    builder.AddSymbol(vowel, true);
                }
                var consonants = GetConsonants();
                foreach (var consonant in consonants) {
                    builder.AddSymbol(consonant, false);
                }
                var replacements = GetDictionaryPhonemesReplacement();
                builder.AddEntry("a", new string[] { "a" });

                dictionary.Split('\n')
                        .Where(line => !line.StartsWith(";;;"))
                        .Select(line => line.Trim())
                        .Select(line => line.Split(new string[] { "  " }, StringSplitOptions.None))
                        .Where(parts => parts.Length == 2)
                        .ToList()
                        .ForEach(parts => builder.AddEntry(parts[0].ToLowerInvariant(), GetDictionaryWordPhonemes(parts[1]).Select(
                            n =>  replacements != null && replacements.ContainsKey(n) ? replacements[n] : n)));
                var dict = builder.Build();
                dictionaries[GetType()] = dict;
            }
            catch (Exception ex) { }
        }

        private string[] ApplyExtensions(string[] symbols, Note[] notes) {
            var newSymbols = new List<string>();
            var vowelIds = ExtractVowels(symbols);
            if (vowelIds.Count == 0) {
                // no syllables or all consonants, the last phoneme will be interpreted as vowel
                vowelIds.Add(symbols.Length - 1);
            }
            var lastVowelI = 0;
            newSymbols.AddRange(symbols.Take(vowelIds[lastVowelI] + 1));
            for (var i = 1; i < notes.Length && i < notes.Length && lastVowelI + 1 < vowelIds.Count; i++) {
                if (!IsSyllableExtensionNote(notes[i])) {
                    var prevVowel = vowelIds[lastVowelI];
                    lastVowelI++;
                    var vowel = vowelIds[lastVowelI];
                    newSymbols.AddRange(symbols.Skip(prevVowel + 1).Take(vowel - prevVowel));
                } else {
                    newSymbols.Add(symbols[vowelIds[lastVowelI]]);
                }
            }
            newSymbols.AddRange(symbols.Skip(vowelIds[lastVowelI] + 1));
            return newSymbols.ToArray();
        }

        private List<int> ExtractVowels(string[] symbols) {
            var vowelIds = new List<int>();
            var vowels = GetVowels();
            for (var i = 0; i < symbols.Length; i++) {
                if (vowels.Contains(symbols[i])) {
                    vowelIds.Add(i);
                }
            }
            return vowelIds;
        }

        private int GetNoteLength(string alias, int phonemesCount, int containerLength = -1) {
            var noteLength = GetTransitionBasicLength(alias);
            if (containerLength == -1) {
                return (int)(noteLength / 5 * 5);
            }

            var fullLength = noteLength * 2.5 + noteLength * phonemesCount;
            if (fullLength <= containerLength) {
                return (int)(noteLength / 5 * 5);
            }
            return (int)(containerLength / fullLength * noteLength) / 5 * 5;
        }

        private Phoneme[] MakePhonemes(List<string> phonemeSymbols, int containerLength, int position, int tone, int lastTone, bool isEnding) {
            var phonemes = new Phoneme[phonemeSymbols.Count];
            var offset = 0;
            for (var i = 0; i < phonemeSymbols.Count; i++) {
                var phonemeI = phonemeSymbols.Count - i - 1;
                var validatedAlias = ValidateAlias(phonemeSymbols[phonemeI]);

                var noteLengthTick = GetNoteLength(validatedAlias, isEnding ? phonemeSymbols.Count : phonemeSymbols.Count - 1, containerLength);
                if (!isEnding && i == 0) {
                    noteLengthTick = 0;
                }

                var currentTone = phonemeI == phonemeSymbols.Count - 1 ? lastTone : tone;
                phonemes[phonemeI].phoneme = MapPhoneme(validatedAlias, currentTone, singer);
                phonemes[phonemeI].position = position - noteLengthTick - offset;
                offset += noteLengthTick;
            }
            return phonemes;
        }

        #endregion
    }
}
