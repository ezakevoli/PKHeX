﻿using System;
using System.Collections.Generic;
using System.Linq;

namespace PKHeX.Core
{
    public enum Severity
    {
        Indeterminate = -2,
        Invalid = -1,
        Fishy = 0,
        Valid = 1,
        NotImplemented = 2,
    }
    internal enum CheckIdentifier
    {
        Move,
        RelearnMove,
        Encounter,
        History,
        ECPID,
        Shiny,
        EC,
        PID,
        Gender,
        EVs,
        Language,
        Nickname,
        Trainer,
        IVs,
        None,
        Level,
        Ball,
        Memory,
        Geography,
        Form,
        Egg,
        Misc,
        Fateful,
        Ribbon,
        Training,
        Ability,
        Evolution,
        Special
    }
    public class CheckResult
    {
        internal readonly Severity Judgement = Severity.Valid;
        internal string Comment = "Valid";
        public bool Valid => Judgement >= Severity.Fishy;
        public bool Flag;
        private readonly CheckIdentifier Identifier;

        internal CheckResult(CheckIdentifier i) { Identifier = i; }
        internal CheckResult(Severity s, string c, CheckIdentifier i)
        {
            Judgement = s;
            Comment = c;
            Identifier = i;
        }
    }
    public partial class LegalityAnalysis
    {
        private void verifyGender()
        {
            if (pkm.PersonalInfo.Gender == 255 && pkm.Gender != 2)
            {
                AddLine(Severity.Invalid, "Genderless Pokémon should not have a gender.", CheckIdentifier.Gender);
                // return;
            }
        }

        private void verifyItem()
        {
            if (!Legal.getHeldItemAllowed(pkm.Format, pkm.HeldItem))
                AddLine(Severity.Invalid, "Held item is unreleased.", CheckIdentifier.Form);
        }

        private void verifyECPID()
        {
            if (pkm.EncryptionConstant == 0)
                AddLine(Severity.Fishy, "Encryption Constant is not set.", CheckIdentifier.EC);

            if (pkm.PID == 0)
                AddLine(Severity.Fishy, "PID is not set.", CheckIdentifier.PID);

            if (pkm.GenNumber >= 6 && pkm.PID == pkm.EncryptionConstant)
                AddLine(Severity.Fishy, "Encryption Constant matches PID.", CheckIdentifier.PID);

            if (EncounterType == typeof (EncounterStatic))
            {
                var enc = (EncounterStatic) EncounterMatch;
                if (enc.Shiny != null && (bool) enc.Shiny ^ pkm.IsShiny)
                {
                    AddLine(Severity.Invalid, $"Encounter {(enc.Shiny == true ? "must be" : "cannot be")} shiny.", CheckIdentifier.Shiny);
                    return;
                }
            }

            int wIndex = Array.IndexOf(Legal.WurmpleEvolutions, pkm.Species);
            if (pkm.GenNumber >= 6)
            {
                // Wurmple -> Silcoon/Cascoon
                if (wIndex > -1)
                {
                    // Check if Wurmple was the origin (only Egg and Wild Encounter)
                    if (pkm.WasEgg || (EncounterType == typeof(EncounterSlot[]) && (EncounterMatch as EncounterSlot[]).All(slot => slot.Species == 265)))
                        if ((pkm.EncryptionConstant >> 16)%10/5 != wIndex/2)
                        {
                            AddLine(Severity.Invalid, "Wurmple evolution Encryption Constant mismatch.", CheckIdentifier.EC);
                            return;
                        }
                }
                else if (pkm.Species == 265)
                {
                    AddLine(Severity.Valid, "Wurmple Evolution: " + ((pkm.EncryptionConstant >> 16)%10/5 == 0 ? "Silcoon" : "Cascoon"), CheckIdentifier.EC);
                }
                
                int xor = pkm.TSV ^ pkm.PSV;
                if (xor < 16 && xor >= 8 && (pkm.PID ^ 0x80000000) == pkm.EncryptionConstant)
                {
                    AddLine(Severity.Fishy, "Encryption Constant matches shinyxored PID.", CheckIdentifier.EC);
                    return;
                }
            }
            
            // abort if specimen wasn't transferred x->6
            if (pkm.Format < 6 || !(3 <= pkm.GenNumber && pkm.GenNumber <= 5))
                return;

            // When transferred to Generation 6, the Encryption Constant is copied from the PID.
            // The PID is then checked to see if it becomes shiny with the new Shiny rules (>>4 instead of >>3)
            // If the PID is nonshiny->shiny, the top bit is flipped.

            // Check to see if the PID and EC are properly configured.
            bool xorPID = ((pkm.TID ^ pkm.SID ^ (int)(pkm.PID & 0xFFFF) ^ (int)(pkm.PID >> 16)) & ~0x7) == 8;
            bool valid = xorPID
                ? pkm.EncryptionConstant == (pkm.PID ^ 0x8000000)
                : pkm.EncryptionConstant == pkm.PID;

            if (!valid)
            {
                AddLine(Severity.Invalid,
                    xorPID 
                    ? "PID should be equal to EC [with top bit flipped]!" 
                    : "PID should be equal to EC!", CheckIdentifier.ECPID);
            }
        }
        private void verifyNickname()
        {
            // If the Pokémon is not nicknamed, it should match one of the language strings.
            if (pkm.Nickname.Length == 0)
            {
                AddLine(Severity.Invalid, "Nickname is empty.", CheckIdentifier.Nickname);
                return;
            }
            if (pkm.Species > PKX.SpeciesLang[0].Length)
            {
                AddLine(Severity.Indeterminate, "Species index invalid for Nickname comparison.", CheckIdentifier.Nickname);
                return;
            }

            if (pkm.VC)
            {
                string pk = pkm.Nickname;
                var langset = PKX.SpeciesLang.FirstOrDefault(s => s.Contains(pk)) ?? PKX.SpeciesLang[2];
                int lang = Array.IndexOf(PKX.SpeciesLang, langset);

                if (pk.Length > (lang == 2 ? 10 : 5))
                    AddLine(Severity.Invalid, "Nickname too long.", CheckIdentifier.Nickname);
            }

            if (!Encounter.Valid)
                return;

            if (pkm.Format <= 6 && pkm.Language > 8)
            {
                AddLine(Severity.Indeterminate, "Language ID > 8.", CheckIdentifier.Language);
                return;
            }
            if (pkm.Format <= 7 && pkm.Language > 10)
            {
                AddLine(Severity.Indeterminate, "Language ID > 10.", CheckIdentifier.Language);
                return;
            }

            if (EncounterType == typeof(EncounterTrade))
            {
                string[] validOT = new string[0];
                int index = -1;
                if (pkm.XY)
                {
                    validOT = Legal.TradeXY[pkm.Language];
                    index = Array.IndexOf(Legal.TradeGift_XY, EncounterMatch);
                }
                else if (pkm.AO)
                {
                    validOT = Legal.TradeAO[pkm.Language];
                    index = Array.IndexOf(Legal.TradeGift_AO, EncounterMatch);
                }
                else if (pkm.SM)
                {
                    // TODO
                    AddLine(Severity.Valid, "Ingame Trade for Sun/Moon not implemented.", CheckIdentifier.Nickname);
                    return;
                }
                else if (pkm.Format <= 2 || pkm.VC)
                {
                    var et = EncounterOriginal as EncounterTrade;
                    if (et?.TID == 0) // Gen1 Trade
                    {
                        if (!Legal.getEncounterTrade1Valid(pkm, et))
                            AddLine(Severity.Invalid, "Incorrect OT name for RBY in-game trade.", CheckIdentifier.Trainer);
                    }
                    else // Gen2
                    {
                        return; // already checked all relevant properties when fetching with getValidEncounterTradeVC2
                    }
                    return;
                }

                if (validOT.Length == 0)
                {
                    AddLine(Severity.Indeterminate, "Ingame Trade invalid version?", CheckIdentifier.Trainer);
                    return;
                }
                if (index == -1 || validOT.Length < index*2)
                {
                    AddLine(Severity.Indeterminate, "Ingame Trade invalid lookup?", CheckIdentifier.Trainer);
                    return;
                }

                string nick = validOT[index];
                string OT = validOT[validOT.Length/2 + index];

                if (nick != pkm.Nickname)
                    AddLine(Severity.Fishy, "Ingame Trade nickname has been altered.", CheckIdentifier.Nickname);
                else if (OT != pkm.OT_Name)
                    AddLine(Severity.Invalid, "Ingame Trade OT has been altered.", CheckIdentifier.Trainer);
                else
                    AddLine(Severity.Valid, "Ingame Trade OT/Nickname have not been altered.", CheckIdentifier.Nickname);

                return;
            }

            if (pkm.IsEgg)
            {
                if (!pkm.IsNicknamed && (pkm.Format != 7))
                    AddLine(Severity.Invalid, "Eggs must be nicknamed.", CheckIdentifier.Egg);
                else if (PKX.SpeciesLang[pkm.Language][0] != pkm.Nickname)
                    AddLine(Severity.Invalid, "Egg name does not match language Egg name.", CheckIdentifier.Egg);
                else
                    AddLine(Severity.Valid, "Egg matches language Egg name.", CheckIdentifier.Egg);

                return;
            }

            string nickname = pkm.Nickname.Replace("'", "’");
            if (pkm.IsNicknamed)
            {
                for (int i = 0; i < PKX.SpeciesLang.Length; i++)
                {
                    string[] lang = PKX.SpeciesLang[i];
                    int index = Array.IndexOf(lang, nickname);
                    if (index < 0)
                        continue;

                    AddLine(Severity.Fishy, index == pkm.Species && i != pkm.Language
                        ? "Nickname matches another species name (+language)."
                        : "Nickname flagged, matches species name.", CheckIdentifier.Nickname);
                    return;
                }
                AddLine(Severity.Valid, "Nickname does not match another species name.", CheckIdentifier.Nickname);
            }
            else if (pkm.Format < 3)
            {
                // pk1/pk2 IsNicknamed getter checks for match, logic should only reach here if matches.
                AddLine(Severity.Valid, "Nickname matches species name.", CheckIdentifier.Nickname);
            }
            else
            {
                // Can't have another language name if it hasn't evolved or wasn't a language-traded egg.
                bool match = (pkm.WasTradedEgg || Legal.getHasEvolved(pkm)) && PKX.SpeciesLang.Any(lang => lang[pkm.Species] == nickname)
                       || PKX.SpeciesLang[pkm.Language][pkm.Species] == nickname;

                if (!match)
                {
                    if ((EncounterMatch as MysteryGift)?.CardID == 2046 && (pkm.SID << 16 | pkm.TID) == 0x79F57B49)
                        AddLine(Severity.Valid, "Nickname matches demo language name.", CheckIdentifier.Nickname);
                    else
                        AddLine(Severity.Invalid, "Nickname does not match species name.", CheckIdentifier.Nickname);
                }
                else
                    AddLine(Severity.Valid, "Nickname matches species name.", CheckIdentifier.Nickname);
            }
        }
        private void verifyEVs()
        {
            var evs = pkm.EVs;
            int sum = evs.Sum();
            if (pkm.IsEgg && sum > 0)
                AddLine(Severity.Invalid, "Eggs cannot receive EVs.", CheckIdentifier.EVs);
            else if (sum == 0 && pkm.Stat_Level - pkm.Met_Level > 0)
                AddLine(Severity.Fishy, "All EVs are zero, but leveled above Met Level.", CheckIdentifier.EVs);
            else if (sum == 508)
                AddLine(Severity.Fishy, "2 EVs remaining.", CheckIdentifier.EVs);
            else if (sum > 510)
                AddLine(Severity.Invalid, "EV total cannot be above 510.", CheckIdentifier.EVs);
            else if (pkm.Format >= 6 && evs.Any(ev => ev > 252))
                AddLine(Severity.Invalid, "EVs cannot go above 252.", CheckIdentifier.EVs);
            else if (evs.All(ev => pkm.EVs[0] == ev) && evs[0] != 0)
                AddLine(Severity.Fishy, "EVs are all equal.", CheckIdentifier.EVs);
        }
        private void verifyIVs()
        {
            if ((EncounterMatch as EncounterStatic)?.IV3 == true)
            {
                if (pkm.IVs.Count(iv => iv == 31) < 3)
                {
                    AddLine(Severity.Invalid, "Should have at least 3 IVs = 31.", CheckIdentifier.IVs);
                    return;
                }
            }
            if ((EncounterMatch as EncounterSlot[])?.All(slot => slot.Type == SlotType.FriendSafari) == true)
            {
                if (pkm.IVs.Count(iv => iv == 31) < 2)
                {
                    AddLine(Severity.Invalid, "Friend Safari captures should have at least 2 IVs = 31.", CheckIdentifier.IVs);
                    return;
                }
            }
            if (EncounterIsMysteryGift)
            {
                int[] IVs;
                switch (((MysteryGift) EncounterMatch).Format)
                {
                    case 6: IVs = ((WC6)EncounterMatch).IVs; break;
                    case 7: IVs = ((WC7)EncounterMatch).IVs; break;
                    default: IVs = null; break;
                }

                if (IVs != null)
                {
                    var pkIVs = pkm.IVs;
                    bool valid = true;
                    for (int i = 0; i < 6; i++)
                        if (IVs[i] <= 31 && IVs[i] != pkIVs[i])
                            valid = false;
                    if (!valid)
                        AddLine(Severity.Invalid, "IVs do not match Mystery Gift Data.", CheckIdentifier.IVs);
                }
            }
            if (pkm.IVs.Sum() == 0)
                AddLine(Severity.Fishy, "All IVs are zero.", CheckIdentifier.IVs);
            else if (pkm.IVs[0] < 30 && pkm.IVs.All(iv => pkm.IVs[0] == iv))
                AddLine(Severity.Fishy, "All IVs are equal.", CheckIdentifier.IVs);
        }
        private void verifyDVs()
        {
            // todo
        }
        private void verifyOT()
        {
            if (EncounterType == typeof(EncounterTrade))
                return; // Already matches Encounter Trade information

            if (pkm.TID == 0 && pkm.SID == 0)
                AddLine(Severity.Fishy, "TID and SID are zero.", CheckIdentifier.Trainer);
            else if (pkm.VC)
            {
                if (pkm.SID != 0)
                    AddLine(Severity.Invalid, "SID should be 0.", CheckIdentifier.Trainer);
            }
            else if (pkm.TID == pkm.SID)
                AddLine(Severity.Fishy, "TID and SID are equal.", CheckIdentifier.Trainer);
            else if (pkm.TID == 0)
                AddLine(Severity.Fishy, "TID is zero.", CheckIdentifier.Trainer);
            else if (pkm.SID == 0)
                AddLine(Severity.Fishy, "SID is zero.", CheckIdentifier.Trainer);

            if (pkm.VC)
                verifyG1OT();
        }
        private void verifyG1OT()
        {
            string tr = pkm.OT_Name;
            string pk = pkm.Nickname;
            var langset = PKX.SpeciesLang.FirstOrDefault(s => s.Contains(pk)) ?? PKX.SpeciesLang[2];
            int lang = Array.IndexOf(PKX.SpeciesLang, langset);

            if (tr.Length > (lang == 2 ? 7 : 5))
                AddLine(Severity.Invalid, "OT Name too long.", CheckIdentifier.Trainer);
            if (pkm.Species == 151)
            {
                if (tr != "GF" && tr != "ゲーフリ") // if there are more events with special OTs, may be worth refactoring
                    AddLine(Severity.Invalid, "Incorrect event OT Name.", CheckIdentifier.Trainer);
            }
        }

        private void verifyHyperTraining()
        {
            if (pkm.Format < 7)
                return; // No Hyper Training before Gen VII

            var IVs = new[] { pkm.IV_HP, pkm.IV_ATK, pkm.IV_DEF, pkm.IV_SPA, pkm.IV_SPD, pkm.IV_SPE };
            var HTs = new[] { pkm.HT_HP, pkm.HT_ATK, pkm.HT_DEF, pkm.HT_SPA, pkm.HT_SPD, pkm.HT_SPE };

            if (HTs.Any(ht => ht) && pkm.CurrentLevel != 100)
                AddLine(Severity.Invalid, "Can't Hyper Train a pokemon that isn't level 100.", CheckIdentifier.IVs);

            if (IVs.All(iv => iv == 31) && HTs.Any(ht => ht))
                AddLine(Severity.Invalid, "Can't Hyper Train a pokemon with perfect IVs.", CheckIdentifier.IVs);
            else
            {
                for (int i = 0; i < 6; i++) // Check individual IVs
                {
                    if ((IVs[i] == 31) && HTs[i])
                        AddLine(Severity.Invalid, "Can't Hyper Train a perfect IV.", CheckIdentifier.IVs);
                }
            }
        }

        private CheckResult verifyEncounterLink()
        {
            // Should NOT be Fateful, and should be in Database
            EncounterLink enc = EncounterMatch as EncounterLink;
            if (enc == null)
                return new CheckResult(Severity.Invalid, "Invalid Link Gift: unable to find matching gift.", CheckIdentifier.Encounter);

            if (pkm.XY && !enc.XY)
                return new CheckResult(Severity.Invalid, "Invalid Link Gift: can't obtain in XY.", CheckIdentifier.Encounter);
            if (pkm.AO && !enc.ORAS)
                return new CheckResult(Severity.Invalid, "Invalid Link Gift: can't obtain in ORAS.", CheckIdentifier.Encounter);
            if (pkm.SM && !enc.SM)
                return new CheckResult(Severity.Invalid, "Invalid Link Gift: can't obtain in SM.", CheckIdentifier.Encounter);

            if (enc.Shiny != null && (bool)enc.Shiny ^ pkm.IsShiny)
                return new CheckResult(Severity.Invalid, "Shiny Link gift mismatch.", CheckIdentifier.Encounter);

            return pkm.FatefulEncounter
                ? new CheckResult(Severity.Invalid, "Invalid Link Gift: should not be Fateful Encounter.", CheckIdentifier.Encounter)
                : new CheckResult(Severity.Valid, "Valid Link gift.", CheckIdentifier.Encounter);
        }
        private CheckResult verifyEncounterEvent()
        {
            MysteryGift MatchedGift = EncounterMatch as MysteryGift;
            if (MatchedGift != null)
                return new CheckResult(Severity.Valid, $"Matches #{MatchedGift.CardID:0000} ({MatchedGift.CardTitle})", CheckIdentifier.Encounter);
            return null;
        }
        private CheckResult verifyEncounterEgg()
        {
            if (pkm.GenNumber < 4)
            {
                if (pkm.Format <= 2) // can't check anything
                    return new CheckResult(CheckIdentifier.Encounter);
                
                if (pkm.HasOriginalMetLocation)
                {

                }
                return new CheckResult(CheckIdentifier.Encounter);
            }

            // Check Hatch Locations
            if (pkm.Met_Level != 1)
                return new CheckResult(Severity.Invalid, "Invalid met level, expected 1.", CheckIdentifier.Encounter);
            // Check species
            if (Legal.NoHatchFromEgg.Contains(pkm.Species))
                return new CheckResult(Severity.Invalid, "Species cannot be hatched from an egg.", CheckIdentifier.Encounter);
            if (pkm.IsEgg)
            {
                if (pkm.Egg_Location == 30002)
                    return new CheckResult(Severity.Invalid, "Egg location shouldn't be 'traded' for an un-hatched egg.", CheckIdentifier.Encounter);

                if (pkm.Met_Location == 30002)
                    return new CheckResult(Severity.Valid, "Valid traded un-hatched egg.", CheckIdentifier.Encounter);
                return pkm.Met_Location == 0
                    ? new CheckResult(Severity.Valid, "Valid un-hatched egg.", CheckIdentifier.Encounter)
                    : new CheckResult(Severity.Invalid, "Invalid location for un-hatched egg (expected no met location).", CheckIdentifier.Encounter);
            }
            if (pkm.XY)
            {
                if (pkm.Egg_Location == 318)
                    return new CheckResult(Severity.Invalid, "Invalid X/Y egg location.", CheckIdentifier.Encounter);
                return Legal.ValidMet_XY.Contains(pkm.Met_Location)
                    ? new CheckResult(Severity.Valid, "Valid X/Y hatched egg.", CheckIdentifier.Encounter)
                    : new CheckResult(Severity.Invalid, "Invalid X/Y location for hatched egg.", CheckIdentifier.Encounter);
            }
            if (pkm.AO)
            {
                return Legal.ValidMet_AO.Contains(pkm.Met_Location)
                    ? new CheckResult(Severity.Valid, "Valid OR/AS hatched egg.", CheckIdentifier.Encounter)
                    : new CheckResult(Severity.Invalid, "Invalid OR/AS location for hatched egg.", CheckIdentifier.Encounter);
            }
            if (pkm.SM)
            {
                return Legal.ValidMet_SM.Contains(pkm.Met_Location)
                    ? new CheckResult(Severity.Valid, "Valid S/M hatched egg.", CheckIdentifier.Encounter)
                    : new CheckResult(Severity.Invalid, "Invalid S/M location for hatched egg.", CheckIdentifier.Encounter);
            }
            return new CheckResult(Severity.Invalid, "Invalid location for hatched egg.", CheckIdentifier.Encounter);
        }
        private CheckResult verifyEncounterSafari()
        {
            if (pkm.Species == 670 || pkm.Species == 671) // Floette
                if (!new[] {0, 1, 3}.Contains(pkm.AltForm)) // 0/1/3 - RBY
                    return new CheckResult(Severity.Invalid, "Friend Safari: Not valid color.", CheckIdentifier.Encounter);
            else if (pkm.Species == 710 || pkm.Species == 711) // Pumpkaboo
                if (pkm.AltForm != 1) // Average
                    return new CheckResult(Severity.Invalid, "Friend Safari: Not average sized.", CheckIdentifier.Encounter);
            else if (pkm.Species == 586) // Sawsbuck
                if (pkm.AltForm != 0)
                    return new CheckResult(Severity.Invalid, "Friend Safari: Not Spring form.", CheckIdentifier.Encounter);

            return new CheckResult(Severity.Valid, "Valid Friend Safari encounter.", CheckIdentifier.Encounter);
        }
        private CheckResult verifyEncounterWild()
        {
            EncounterSlot[] enc = (EncounterSlot[])EncounterMatch;

            if (enc.Any(slot => slot.Normal))
                return enc.All(slot => slot.Pressure)
                    ? new CheckResult(Severity.Valid, "Valid encounter at location (Pressure/Hustle/Vital Spirit).", CheckIdentifier.Encounter)
                    : new CheckResult(Severity.Valid, "Valid encounter at location.", CheckIdentifier.Encounter);

            // Decreased Level Encounters
            if (enc.Any(slot => slot.WhiteFlute))
                return enc.All(slot => slot.Pressure)
                    ? new CheckResult(Severity.Valid, "Valid encounter at location (White Flute & Pressure/Hustle/Vital Spirit).", CheckIdentifier.Encounter)
                    : new CheckResult(Severity.Valid, "Valid encounter at location (White Flute).", CheckIdentifier.Encounter);

            // Increased Level Encounters
            if (enc.Any(slot => slot.BlackFlute))
                return enc.All(slot => slot.Pressure)
                    ? new CheckResult(Severity.Valid, "Valid encounter at location (Black Flute & Pressure/Hustle/Vital Spirit).", CheckIdentifier.Encounter)
                    : new CheckResult(Severity.Valid, "Valid encounter at location (Black Flute).", CheckIdentifier.Encounter);

            if (enc.Any(slot => slot.Pressure))
                return new CheckResult(Severity.Valid, "Valid encounter at location (Pressure/Hustle/Vital Spirit).", CheckIdentifier.Encounter);

            return new CheckResult(Severity.Valid, "Valid encounter at location (DexNav).", CheckIdentifier.Encounter);
        }
        private CheckResult verifyEncounterStatic()
        {
            var s = (EncounterStatic)EncounterMatch;

            // Re-parse relearn moves
            if (s.EggLocation != 60002 || vRelearn.Any(rl => !rl.Valid))
            {
                for (int i = 0; i < 4; i++)
                    vRelearn[i] = pkm.RelearnMoves[i] != s.Relearn[i]
                        ? new CheckResult(Severity.Invalid, "Static encounter relearn move mismatch.", CheckIdentifier.RelearnMove)
                        : new CheckResult(CheckIdentifier.RelearnMove);
                return new CheckResult(Severity.Valid, "Valid gift/static encounter.", CheckIdentifier.Encounter);
            }
            return null;
        }
        private CheckResult verifyEncounterTrade()
        {
            return new CheckResult(Severity.Valid, "Valid ingame trade.", CheckIdentifier.Encounter);
        }
        private CheckResult verifyEncounterG12()
        {
            var obj = Legal.getEncounter12(pkm, pkm.Format < 3);
            if (obj == null)
                return new CheckResult(Severity.Invalid, "Unknown encounter.", CheckIdentifier.Encounter);

            EncounterMatch = obj.Item1;
            if (EncounterMatch is bool)
            {
                pkm.WasEgg = true;
                return verifyEncounterEgg();
            }
            if (EncounterMatch is EncounterSlot[])
                return verifyEncounterWild();
            if (EncounterMatch is EncounterStatic)
                return verifyEncounterStatic();
            if (EncounterMatch is EncounterTrade)
                return verifyEncounterTrade();

            // shouldn't ever hit, above 3*invalid check should abort
            Console.WriteLine($"Gen1 encounter fallthrough: {pkm.FileName}");
            return new CheckResult(Severity.Invalid, "Unknown encounter.", CheckIdentifier.Encounter);
        }
        private CheckResult verifyEncounter()
        {
            if (pkm.VC || pkm.Format < 3)
            {
                int baseSpecies = Legal.getBaseSpecies(pkm);
                bool g1 = pkm.VC1 || pkm.Format == 1;
                
                if ((g1 && baseSpecies > Legal.MaxSpeciesID_1) || (baseSpecies > Legal.MaxSpeciesID_2))
                    return new CheckResult(Severity.Invalid, "VC: Unobtainable species.", CheckIdentifier.Encounter);
                
                // Get EncounterMatch prior to parsing transporter legality
                var result = verifyEncounterG12();
                EncounterOriginal = EncounterMatch;

                if (pkm.Format > 2) // transported to 7+
                    AddLine(verifyVCEncounter(baseSpecies));

                return result;
            }
            
            if (pkm.WasLink)
                return verifyEncounterLink();

            bool wasEvent = pkm.WasEvent || pkm.WasEventEgg;
            if (wasEvent)
            {
                var result = verifyEncounterEvent();
                if (result != null)
                    return result;
            }
            
            if (null != (EncounterMatch = Legal.getValidStaticEncounter(pkm)))
            {
                var result = verifyEncounterStatic();
                if (result != null)
                    return result;

                EncounterMatch = null; // Reset Encounter Object, test for remaining encounters
            }
            
            if (pkm.WasEgg)
                return verifyEncounterEgg();
            
            if (null != (EncounterMatch = Legal.getValidFriendSafari(pkm)))
                return verifyEncounterSafari();
            
            if (null != (EncounterMatch = Legal.getValidWildEncounters(pkm)))
                return verifyEncounterWild();
            
            if (null != (EncounterMatch = Legal.getValidIngameTrade(pkm)))
                return verifyEncounterTrade();

            return wasEvent 
                ? new CheckResult(Severity.Invalid, "Unable to match to a Mystery Gift in the database.", CheckIdentifier.Encounter) 
                : new CheckResult(Severity.Invalid, "Unknown encounter.", CheckIdentifier.Encounter);
        }
        private CheckResult verifyVCEncounter(int baseSpecies)
        {
            // Sanitize Species to non-future species#
            int species = pkm.Species;
            if ((pkm.VC1 && species > Legal.MaxSpeciesID_1) ||
                (pkm.VC2 && species > Legal.MaxSpeciesID_2))
                species = baseSpecies;

            // Check existing EncounterMatch
            if ((EncounterOriginal ?? EncounterMatch) == null)
                return new CheckResult(Severity.Invalid, "Unable to match an encounter from origin game.", CheckIdentifier.Encounter);

            var s = EncounterMatch as EncounterStatic;
            if (s != null && s.Version == GameVersion.SPECIAL)
            {
                bool exceptions = false;
                exceptions |= baseSpecies == 151 && pkm.TID == 22796;
                if (!exceptions)
                    AddLine(new CheckResult(Severity.Invalid, "Special encounter is not available to Virtual Console games.", CheckIdentifier.Encounter));
            }
            
            EncounterMatch = new EncounterStatic
            {
                Species = species,
                Gift = true, // Forces Poké Ball
                Ability = Legal.TransferSpeciesDefaultAbility_1.Contains(species) ? 1 : 4, // Hidden by default, else first
                Shiny = species == 151 ? (bool?)false : null,
                Fateful = species == 151,
                Location = 30013,
                EggLocation = 0,
                Version = GameVersion.RBY
            };
            var ematch = (EncounterStatic) EncounterMatch;

            if (pkm.Met_Location != ematch.Location)
                return new CheckResult(Severity.Invalid, "Invalid met location.", CheckIdentifier.Encounter);
            if (pkm.Egg_Location != ematch.EggLocation)
                return new CheckResult(Severity.Invalid, "Should not have an egg location.", CheckIdentifier.Encounter);

            if (species == 150 && pkm.Moves.Contains(6))
                return new CheckResult(Severity.Invalid, "Mewtwo cannot be transferred while knowing Pay Day.", CheckIdentifier.Encounter);

            return new CheckResult(CheckIdentifier.Encounter);
        }

        private void verifyLevel()
        {
            MysteryGift MatchedGift = EncounterMatch as MysteryGift;
            if (MatchedGift != null && MatchedGift.Level != pkm.Met_Level)
            {
                if (!(MatchedGift is WC7) || ((WC7) MatchedGift).MetLevel != pkm.Met_Level)
                {
                    AddLine(new CheckResult(Severity.Invalid, "Met Level does not match Wonder Card level.", CheckIdentifier.Level));
                    return;
                }
            }
            if (MatchedGift != null && MatchedGift.Level > pkm.CurrentLevel)
            {
                AddLine(new CheckResult(Severity.Invalid, "Current Level below Wonder Card level.", CheckIdentifier.Level));
                return;
            }

            int lvl = pkm.CurrentLevel;
            if (lvl > 1 && pkm.IsEgg)
                AddLine(Severity.Invalid, "Current level for an egg is invalid.", CheckIdentifier.Level);
            else if (lvl < pkm.Met_Level)
                AddLine(Severity.Invalid, "Current level is below met level.", CheckIdentifier.Level);
            else if ((pkm.WasEgg || EncounterMatch == null) && !Legal.getEvolutionValid(pkm) && pkm.Species != 350)
                AddLine(Severity.Invalid, "Evolution not valid (or level/trade evolution unsatisfied).", CheckIdentifier.Level);
            else if (lvl > pkm.Met_Level && lvl > 1 && lvl != 100 && pkm.EXP == PKX.getEXP(pkm.Stat_Level, pkm.Species))
                AddLine(Severity.Fishy, "Current experience matches level threshold.", CheckIdentifier.Level);
            else
                AddLine(Severity.Valid, "Current level is not below met level.", CheckIdentifier.Level);
        }
        private void verifyMedals()
        {
            if (pkm.Format < 6)
                return;

            // Training Medals
            var TrainNames = ReflectUtil.getPropertiesStartWithPrefix(pkm.GetType(), "SuperTrain").ToArray();
            var TrainCount = TrainNames.Count(MissionName => ReflectUtil.GetValue(pkm, MissionName) as bool? == true);
            if (pkm.IsEgg && TrainCount > 0)
            { AddLine(Severity.Invalid, "Super Training missions on Egg.", CheckIdentifier.Training); }
            else if (TrainCount > 0 && pkm.GenNumber != 6)
            { AddLine(Severity.Invalid, "Super Training missions are not available in game.", CheckIdentifier.Training); }
            else
            {
                if (pkm.Format >= 7)
                {
                    if (pkm.SecretSuperTrainingUnlocked)
                    { AddLine(Severity.Invalid, "Super Training unlocked flag invalid.", CheckIdentifier.Training); }
                    if (pkm.SecretSuperTrainingComplete)
                    { AddLine(Severity.Invalid, "Super Training complete flag invalid.", CheckIdentifier.Training); }
                }
                else
                {
                    if (TrainCount == 30 ^ pkm.SecretSuperTrainingComplete)
                    { AddLine(Severity.Invalid, "Super Training complete flag mismatch.", CheckIdentifier.Training); }
                }
            }

            // Distribution Training Medals
            var DistNames = ReflectUtil.getPropertiesStartWithPrefix(pkm.GetType(), "DistSuperTrain");
            var DistCount = DistNames.Count(MissionName => ReflectUtil.GetValue(pkm, MissionName) as bool? == true);
            if (pkm.IsEgg && DistCount > 0)
            { AddLine(Severity.Invalid, "Distribution Super Training missions on Egg.", CheckIdentifier.Training); }
            else if (DistCount > 0 && pkm.GenNumber != 6)
            { AddLine(Severity.Invalid, "Distribution Super Training missions are not available in game.", CheckIdentifier.Training); }
            else if (DistCount > 0)
            { AddLine(Severity.Fishy, "Distribution Super Training missions are not released.", CheckIdentifier.Training); }
        }
        private void verifyRibbons()
        {
            if (!Encounter.Valid)
                return;

            List<string> missingRibbons = new List<string>();
            List<string> invalidRibbons = new List<string>();
            
            if (pkm.IsEgg)
            {
                var RibbonNames = ReflectUtil.getPropertiesStartWithPrefix(pkm.GetType(), "Ribbon");
                foreach (object RibbonValue in RibbonNames.Select(RibbonName => ReflectUtil.GetValue(pkm, RibbonName)))
                {
                    if (RibbonValue as bool? == true) // Boolean
                    { AddLine(Severity.Invalid, "Eggs should not have ribbons.", CheckIdentifier.Ribbon); return; }
                    if ((RibbonValue as int?) > 0) // Count
                    { AddLine(Severity.Invalid, "Eggs should not have ribbons.", CheckIdentifier.Ribbon); return; }
                }
                return;
            }

            // Check Event Ribbons
            var RibbonData = ReflectUtil.getPropertiesStartWithPrefix(pkm.GetType(), "Ribbon");
            MysteryGift MatchedGift = EncounterMatch as MysteryGift;
            string[] EventRib =
            {
                nameof(PK6.RibbonCountry), nameof(PK6.RibbonNational), nameof(PK6.RibbonEarth), nameof(PK6.RibbonWorld), nameof(PK6.RibbonClassic),
                nameof(PK6.RibbonPremier), nameof(PK6.RibbonEvent), nameof(PK6.RibbonBirthday), nameof(PK6.RibbonSpecial), nameof(PK6.RibbonSouvenir),
                nameof(PK6.RibbonWishing), nameof(PK6.RibbonChampionBattle), nameof(PK6.RibbonChampionRegional), nameof(PK6.RibbonChampionNational), nameof(PK6.RibbonChampionWorld)
            };
            if (MatchedGift != null) // Wonder Card
            {
                var mgRibbons = ReflectUtil.getPropertiesStartWithPrefix(MatchedGift.Content.GetType(), "Ribbon");
                var commonRibbons = mgRibbons.Intersect(RibbonData).ToArray();

                foreach (string r in commonRibbons)
                {
                    bool? pk = ReflectUtil.getBooleanState(pkm, r);
                    bool? mg = ReflectUtil.getBooleanState(MatchedGift, r);
                    if (pk != mg) // Mismatch
                    {
                        if (pk ?? false)
                            missingRibbons.Add(r);
                        else
                            invalidRibbons.Add(r);
                    }
                }
            }
            else if (EncounterType == typeof(EncounterLink))
            {
                // No Event Ribbons except Classic (unless otherwise specified, ie not for Demo)
                for (int i = 0; i < EventRib.Length; i++)
                {
                    if (i == 4)
                        continue;

                    if (ReflectUtil.getBooleanState(pkm, EventRib[i]) == true)
                        invalidRibbons.Add(EventRibName[i]);
                }

                bool classic = ReflectUtil.getBooleanState(pkm, EventRib[4]) == true;
                if (classic ^ ((EncounterLink)EncounterMatch).Classic)
                    (classic ? invalidRibbons : missingRibbons).Add(EventRibName[4]);
            }
            else if (EncounterType == typeof(EncounterStatic))
            {
                // No Event Ribbons except Wishing (which is only for Magearna)
                for (int i = 0; i < EventRib.Length; i++)
                {
                    if (i == 10)
                        continue;

                    if (ReflectUtil.getBooleanState(pkm, EventRib[i]) == true)
                        invalidRibbons.Add(EventRibName[i]);
                }

                bool wishing = ReflectUtil.getBooleanState(pkm, EventRib[10]) == true;
                if (wishing ^ ((EncounterStatic)EncounterMatch).RibbonWishing)
                    (wishing ? invalidRibbons : missingRibbons).Add(EventRibName[10]);
            }
            else // No ribbons
            {
                for (int i = 0; i < EventRib.Length; i++)
                    if (ReflectUtil.getBooleanState(pkm, EventRib[i]) == true)
                        invalidRibbons.Add(EventRibName[i]);
            }
            
            // Unobtainable ribbons for Gen Origin
            if (pkm.GenNumber > 3)
            {
                if (ReflectUtil.getBooleanState(pkm, nameof(PK3.RibbonChampionG3Hoenn)) == true)
                    invalidRibbons.Add("GBA Champion"); // RSE HoF
                if (ReflectUtil.getBooleanState(pkm, nameof(PK3.RibbonArtist)) == true)
                    invalidRibbons.Add("Artist"); // RSE Master Rank Portrait
                if (ReflectUtil.getBooleanState(pkm, nameof(PK3.RibbonNational)) == true && pkm.Version != (int)GameVersion.CXD)
                    invalidRibbons.Add("National Ribbon (Purified)"); // RSE HoF
            }
            if (pkm.GenNumber > 4)
            {
                if (ReflectUtil.getBooleanState(pkm, nameof(PK4.RibbonChampionSinnoh)) == true)
                    invalidRibbons.Add("Sinnoh Champ"); // DPPt HoF
                if (ReflectUtil.getBooleanState(pkm, nameof(PK4.RibbonLegend)) == true)
                    invalidRibbons.Add("Legend"); // HGSS Defeat Red @ Mt.Silver
            }
            if (pkm.Format >= 6 && pkm.GenNumber >= 6)
            {
                if (ReflectUtil.getBooleanState(pkm, nameof(PK6.RibbonCountMemoryContest)) == true)
                    invalidRibbons.Add("Contest Memory"); // Gen3/4 Contest
                if (ReflectUtil.getBooleanState(pkm, nameof(PK6.RibbonCountMemoryBattle)) == true)
                    invalidRibbons.Add("Battle Memory"); // Gen3/4 Battle
            }
            if (ReflectUtil.getBooleanState(pkm, nameof(PK6.RibbonRecord)) == true)
                invalidRibbons.Add("Record"); // Unobtainable
            
            if (missingRibbons.Count + invalidRibbons.Count == 0)
            {
                AddLine(Severity.Valid, "All ribbons accounted for.", CheckIdentifier.Ribbon);
                return;
            }

            string[] result = new string[2];
            if (missingRibbons.Count > 0)
                result[0] = "Missing Ribbons: " + string.Join(", ", missingRibbons);
            if (invalidRibbons.Count > 0)
                result[1] = "Invalid Ribbons: " + string.Join(", ", invalidRibbons);
            AddLine(Severity.Invalid, string.Join(Environment.NewLine, result.Where(s=>!string.IsNullOrEmpty(s))), CheckIdentifier.Ribbon);
        }
        private void verifyAbility()
        {
            int[] abilities = pkm.PersonalInfo.Abilities;
            int abilval = Array.IndexOf(abilities, pkm.Ability);
            if (abilval < 0)
            {
                AddLine(Severity.Invalid, "Ability is not valid for species/form.", CheckIdentifier.Ability);
                return;
            }

            if (EncounterMatch != null)
            {
                // Check Hidden Ability Mismatches
                if (pkm.GenNumber >= 5)
                {
                    if (EncounterType == typeof(EncounterStatic))
                        if (pkm.AbilityNumber == 4 ^ ((EncounterStatic) EncounterMatch).Ability == 4)
                        {
                            AddLine(Severity.Invalid, "Hidden Ability mismatch for static encounter.", CheckIdentifier.Ability);
                        }
                    if (EncounterType == typeof(EncounterTrade))
                        if (pkm.AbilityNumber == 4 ^ ((EncounterTrade) EncounterMatch).Ability == 4)
                        {
                            AddLine(Severity.Invalid, "Hidden Ability mismatch for ingame trade.", CheckIdentifier.Ability);
                            return;
                        }
                    if (EncounterType == typeof(EncounterLink))
                        if (pkm.AbilityNumber != ((EncounterLink)EncounterMatch).Ability)
                        {
                            AddLine(Severity.Invalid, "Ability mismatch for Link Gift.", CheckIdentifier.Ability);
                            return;
                        }

                }
                if (pkm.GenNumber == 6)
                {
                    if (EncounterIsMysteryGift)
                    {
                        var wc = EncounterMatch as WC6;
                        var type = wc?.AbilityType;
                        if (type < 3 && pkm.AbilityNumber != 1 << type) // set number
                            AddLine(Severity.Invalid, "Ability does not match Mystery Gift.", CheckIdentifier.Ability);
                        else if (type == 3 && pkm.AbilityNumber == 4) // 1/2 only
                            AddLine(Severity.Invalid, "Ability does not match Mystery Gift.", CheckIdentifier.Ability);
                    }
                    if (EncounterType == typeof(EncounterSlot[]) && pkm.AbilityNumber == 4)
                    {
                        var slots = (EncounterSlot[])EncounterMatch;
                        bool valid = slots.Any(slot => slot.DexNav ||
                            slot.Type == SlotType.FriendSafari ||
                            slot.Type == SlotType.Horde);

                        if (!valid)
                        {
                            AddLine(Severity.Invalid, "Hidden Ability on non-horde/friend safari wild encounter.", CheckIdentifier.Ability);
                            return;
                        }
                    }
                }
                if (pkm.GenNumber == 7)
                {
                    if (EncounterIsMysteryGift)
                    {
                        var wc = EncounterMatch as WC7;
                        var type = wc?.AbilityType;
                        if (type < 3 && pkm.AbilityNumber != 1 << type) // set number
                            AddLine(Severity.Invalid, "Ability does not match Mystery Gift.", CheckIdentifier.Ability);
                        else if (type == 3 && pkm.AbilityNumber == 4) // 1/2 only
                            AddLine(Severity.Invalid, "Ability does not match Mystery Gift.", CheckIdentifier.Ability);
                    }
                    if (EncounterType == typeof(EncounterSlot[]) && pkm.AbilityNumber == 4)
                    {
                        var slots = (EncounterSlot[])EncounterMatch;
                        bool valid = slots.Any(slot => slot.Type == SlotType.SOS);

                        if (!valid)
                        {
                            AddLine(Severity.Invalid, "Hidden Ability on non-SOS wild encounter.", CheckIdentifier.Ability);
                            return;
                        }
                    }
                    if (Legal.Ban_NoHidden7.Contains(pkm.Species) && pkm.AbilityNumber == 4)
                    {
                        AddLine(Severity.Invalid, "Hidden Ability not available.", CheckIdentifier.Ability);
                        return;
                    }

                }
            }

            if (3 <= pkm.Format && pkm.Format <= 5) // 3-5
            {
                if (pkm.Version != (int) GameVersion.CXD && abilities[0] != abilities[1] && pkm.PIDAbility != abilval)
                {
                    AddLine(Severity.Invalid, "Ability does not match PID.", CheckIdentifier.Ability);
                    return;
                }
            }
            else
            {
                if (abilities[pkm.AbilityNumber >> 1] != pkm.Ability)
                {
                    AddLine(Severity.Invalid, "Ability does not match ability number.", CheckIdentifier.Ability);
                    return;
                }
            }

            AddLine(Severity.Valid, "Ability matches ability number.", CheckIdentifier.Ability);
        }

        private void verifyBall()
        {
            if (pkm.Format < 3)
                return; // no ball info saved
            if (pkm.GenNumber < 6)
                return; // not implemented

            if (!Encounter.Valid)
                return;

            if (EncounterIsMysteryGift)
            {
                if (pkm.Ball != ((MysteryGift) EncounterMatch).Ball)
                    AddLine(Severity.Invalid, "Ball does not match specified Mystery Gift Ball.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Valid, "Ball matches Mystery Gift.", CheckIdentifier.Ball);

                return;
            }
            if (EncounterType == typeof (EncounterLink))
            {
                if (((EncounterLink)EncounterMatch).Ball != pkm.Ball)
                    AddLine(Severity.Invalid, "Incorrect ball on Link gift.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Valid, "Correct ball on Link gift.", CheckIdentifier.Ball);

                return;
            }
            if (EncounterType == typeof (EncounterTrade))
            {
                if (pkm.Ball != 4) // Pokeball
                    AddLine(Severity.Invalid, "Incorrect ball on ingame trade encounter.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Valid, "Correct ball on ingame trade encounter.", CheckIdentifier.Ball);

                return;
            }
            if (EncounterType == typeof(EncounterStatic))
            {
                EncounterStatic enc = EncounterMatch as EncounterStatic;
                if (enc?.Gift ?? false)
                {
                    if (enc.Ball != pkm.Ball) // Pokéball by default
                        AddLine(Severity.Invalid, "Incorrect ball on ingame gift.", CheckIdentifier.Ball);
                    else
                        AddLine(Severity.Valid, "Correct ball on ingame gift.", CheckIdentifier.Ball);

                    return;
                }

                if (Legal.getWildBalls(pkm).Contains(pkm.Ball))
                    AddLine(Severity.Valid, "Correct ball on ingame static encounter.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Invalid, "Incorrect ball on ingame static encounter.", CheckIdentifier.Ball);

                return;
            }
            if (EncounterType == typeof (EncounterSlot[]))
            {
                if (pkm.Met_Location == 30016 && pkm.Gen7) // Poké Pelago
                {
                    if (pkm.Ball == 0x04)
                        AddLine(Severity.Valid, "Correct ball on Poké Pelago encounter.", CheckIdentifier.Ball);
                    else
                        AddLine(Severity.Invalid, "Incorrect ball on Poké Pelago encounter.", CheckIdentifier.Ball);
                }

                if (Legal.getWildBalls(pkm).Contains(pkm.Ball))
                    AddLine(Severity.Valid, "Correct ball on ingame encounter.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Invalid, "Incorrect ball on ingame encounter.", CheckIdentifier.Ball);

                return;
            }

            if (pkm.WasEgg)
            {
                verifyBallEgg();
                return;
            }

            if (pkm.Ball == 0x04) // Poké Ball
            {
                AddLine(Severity.Valid, "Standard Poké Ball.", CheckIdentifier.Ball);
                return;
            }

            AddLine(Severity.Invalid, "No ball check satisfied, assuming illegal.", CheckIdentifier.Ball);
        }
        private void verifyBallEgg()
        {
            if (pkm.GenNumber < 6) // No inheriting Balls
            {
                if (pkm.Ball != 0x04) // Must be Pokéball -- no ball inheritance.
                    AddLine(Severity.Invalid, "Ball should be Pokéball.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Valid, "Pokéball on egg.", CheckIdentifier.Ball);
                return;
            }

            if (pkm.Ball == 0x01) // Master Ball
            { AddLine(Severity.Invalid, "Master Ball on egg origin.", CheckIdentifier.Ball); return; }
            if (pkm.Ball == 0x10) // Cherish Ball
            { AddLine(Severity.Invalid, "Cherish Ball on non-event.", CheckIdentifier.Ball); return; }
            if (pkm.Ball == 0x04) // Poké Ball
            { AddLine(Severity.Valid, "Standard Poké Ball.", CheckIdentifier.Ball); return; }

            switch (pkm.GenNumber)
            {
                case 6: // Gen6 Inheritance Rules
                    verifyBallEggGen6();
                    return;
                case 7: // Gen7 Inheritance Rules
                    verifyBallEggGen7();
                    return;
            }
        }
        private void verifyBallEggGen6()
        {
            if (pkm.Gender == 2) // Genderless
            {
                if (pkm.Ball != 0x04) // Must be Pokéball as ball can only pass via mother (not Ditto!)
                    AddLine(Severity.Invalid, "Non-Pokéball on genderless egg.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Valid, "Pokéball on genderless egg.", CheckIdentifier.Ball);

                return;
            }
            if (Legal.BreedMaleOnly.Contains(pkm.Species))
            {
                if (pkm.Ball != 0x04) // Must be Pokéball as ball can only pass via mother (not Ditto!)
                    AddLine(Severity.Invalid, "Non-Pokéball on Male-Only egg.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Valid, "Pokéball on Male-Only egg.", CheckIdentifier.Ball);

                return;
            }

            if (pkm.Ball == 0x05) // Safari Ball
            {
                if (Legal.getLineage(pkm).All(e => !Legal.Inherit_Safari.Contains(e)))
                    AddLine(Severity.Invalid, "Safari Ball not possible for species.", CheckIdentifier.Ball);
                else if (pkm.AbilityNumber == 4)
                    AddLine(Severity.Invalid, "Safari Ball with Hidden Ability.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Valid, "Safari Ball possible for species.", CheckIdentifier.Ball);

                return;
            }
            if (0x10 < pkm.Ball && pkm.Ball < 0x18) // Apricorn Ball
            {
                if (Legal.getLineage(pkm).All(e => !Legal.Inherit_Apricorn6.Contains(e)))
                    AddLine(Severity.Invalid, "Apricorn Ball not possible for species.", CheckIdentifier.Ball);
                if (pkm.AbilityNumber == 4)
                    AddLine(Severity.Invalid, "Apricorn Ball with Hidden Ability.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Valid, "Apricorn Ball possible for species.", CheckIdentifier.Ball);

                return;
            }
            if (pkm.Ball == 0x18) // Sport Ball
            {
                if (Legal.getLineage(pkm).All(e => !Legal.Inherit_Sport.Contains(e)))
                    AddLine(Severity.Invalid, "Sport Ball not possible for species.", CheckIdentifier.Ball);
                else if (pkm.AbilityNumber == 4)
                    AddLine(Severity.Invalid, "Sport Ball with Hidden Ability.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Valid, "Sport Ball possible for species.", CheckIdentifier.Ball);

                return;
            }
            if (pkm.Ball == 0x19) // Dream Ball
            {
                if (Legal.getLineage(pkm).All(e => !Legal.Inherit_Dream.Contains(e)))
                    AddLine(Severity.Invalid, "Dream Ball not possible for species.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Valid, "Dream Ball possible for species.", CheckIdentifier.Ball);

                if (pkm.AbilityNumber == 4 && Legal.Ban_DreamHidden.Contains(pkm.Species))
                    AddLine(Severity.Invalid, "Hidden Ability not obtainable from Dream World.", CheckIdentifier.Ball);

                return;
            }
            if (0x0D <= pkm.Ball && pkm.Ball <= 0x0F)
            {
                if (Legal.Ban_Gen4Ball_6.Contains(pkm.Species))
                    AddLine(Severity.Invalid, "Unobtainable capture for Gen4 Ball.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Valid, "Obtainable capture for Gen4 Ball.", CheckIdentifier.Ball);

                return;
            }
            if (0x02 <= pkm.Ball && pkm.Ball <= 0x0C) // Don't worry, Ball # 0x05 was already checked.
            {
                if (Legal.Ban_Gen3Ball.Contains(pkm.Species))
                    AddLine(Severity.Invalid, "Unobtainable capture for Gen3 Ball.", CheckIdentifier.Ball);
                else if (pkm.AbilityNumber == 4 && 152 <= pkm.Species && pkm.Species <= 160)
                    AddLine(Severity.Invalid, "Ball not possible for species with hidden ability.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Valid, "Obtainable capture for Gen3 Ball.", CheckIdentifier.Ball);

                return;
            }

            if (pkm.Species > 650 && pkm.Species != 700) // Sylveon
            {
                if (!Legal.getWildBalls(pkm).Contains(pkm.Ball))
                    AddLine(Severity.Invalid, "Unobtainable ball for Kalos origin.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Valid, "Obtainable ball for Kalos origin.", CheckIdentifier.Ball);
                return;
            }

            AddLine(Severity.Invalid, pkm.Ball >= 26
                    ? "Ball unobtainable in origin generation."
                    : "No ball check satisfied, assuming illegal.", CheckIdentifier.Ball);
        }
        private void verifyBallEggGen7()
        {
            var Lineage = Legal.getLineage(pkm).ToArray();
            if (722 <= pkm.Species && pkm.Species <= 730) // G7 Starters
            {
                if (pkm.Ball == 4)
                    AddLine(Severity.Valid, "Ball possible.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Invalid, "Only Poké Ball possible.", CheckIdentifier.Ball);
                return;
            }

            if (pkm.Ball == 0x05) // Safari Ball
            {
                if (Lineage.Any(e => Legal.Inherit_Safari.Contains(e)))
                    AddLine(Severity.Valid, "Safari Ball possible from Female parent.", CheckIdentifier.Ball);
                else if (Lineage.Any(e => Legal.Inherit_SafariMale.Contains(e)))
                    AddLine(Severity.Valid, "Safari Ball possible from Male parent.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Invalid, "Safari Ball not possible for species.", CheckIdentifier.Ball);

                return;
            }
            if (0x10 < pkm.Ball && pkm.Ball < 0x18) // Apricorn Ball
            {
                if (Lineage.Any(e => Legal.Inherit_Apricorn7.Contains(e))) // past gen
                    AddLine(Severity.Valid, "Apricorn Ball possible for species.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Invalid, "Apricorn Ball not possible for species.", CheckIdentifier.Ball);

                return;
            }
            if (pkm.Ball == 0x18) // Sport Ball
            {
                if (Lineage.All(e => !Legal.Inherit_Sport.Contains(e)))
                    AddLine(Severity.Invalid, "Sport Ball not possible for species.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Valid, "Sport Ball possible for species.", CheckIdentifier.Ball);

                return;
            }
            if (pkm.Ball == 0x19) // Dream Ball
            {
                if (Lineage.Any(e => Legal.Inherit_Dream.Contains(e)))
                    AddLine(Severity.Valid, "Dream Ball inheritance possible from Female species.", CheckIdentifier.Ball);
                else if (Lineage.Any(e => Legal.Inherit_DreamMale.Contains(e)))
                    AddLine(Severity.Valid, "Dream Ball inheritance possible from Male/Genderless species.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Invalid, "Dream Ball not possible for species.", CheckIdentifier.Ball);

                return;
            }
            if (0x0D <= pkm.Ball && pkm.Ball <= 0x0F) // Dusk Heal Quick
            {
                if (Legal.Ban_Gen4Ball_7.Contains(pkm.Species))
                    AddLine(Severity.Invalid, "Unobtainable capture for Gen4 Ball.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Valid, "Obtainable capture for Gen4 Ball.", CheckIdentifier.Ball);

                return;
            }
            if (0x02 <= pkm.Ball && pkm.Ball <= 0x0C) // Don't worry, Ball # 0x05 was already checked.
            {
                if (Legal.Ban_Gen3Ball_7.Contains(pkm.Species))
                    AddLine(Severity.Invalid, "Unobtainable capture for Gen3 Ball.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Valid, "Obtainable capture for Gen3 Ball.", CheckIdentifier.Ball);

                return;
            }

            if (pkm.Ball == 26)
            {
                if ((pkm.Species > 731 && pkm.Species <= 785) || Lineage.Any(e => Legal.PastGenAlolanNatives.Contains(e)))
                {
                    AddLine(Severity.Valid, "Beast Ball possible for species.", CheckIdentifier.Ball);
                    return;
                }
                if (Lineage.Any(e => Legal.PastGenAlolanScans.Contains(e)))
                {
                    AddLine(Severity.Valid, "Scanned Beast Ball possible for species.", CheckIdentifier.Ball);
                    return;
                }
                // next statement catches all new alolans
            }

            if (pkm.Species > 721)
            {
                if (!Legal.getWildBalls(pkm).Contains(pkm.Ball))
                    AddLine(Severity.Invalid, "Unobtainable ball for Alola origin.", CheckIdentifier.Ball);
                else
                    AddLine(Severity.Valid, "Obtainable ball for Alola origin.", CheckIdentifier.Ball);
                return;
            }

            AddLine(Severity.Invalid, pkm.Ball > 26
                    ? "Ball unobtainable in origin generation."
                    : "No ball check satisfied, assuming illegal.", CheckIdentifier.Ball);
        }

        private CheckResult verifyHistory()
        {
            if (!Encounter.Valid)
                return new CheckResult(Severity.Valid, "Skipped History check due to other check being invalid.", CheckIdentifier.History);

            if (pkm.GenNumber < 6)
            {
                if (pkm.Format < 6)
                    return new CheckResult(Severity.Valid, "No History Block to check.", CheckIdentifier.History);

                if (pkm.OT_Affection > 0)
                    return new CheckResult(Severity.Invalid, "OT Affection should be zero.", CheckIdentifier.History);
                if (pkm.OT_Memory > 0 || pkm.OT_Feeling > 0 || pkm.OT_Intensity > 0 || pkm.OT_TextVar > 0)
                    return new CheckResult(Severity.Invalid, "Should not have OT memories.", CheckIdentifier.History);
            }
            
            if (pkm.Format >= 6 && pkm.GenNumber != pkm.Format && pkm.CurrentHandler != 1)
                return new CheckResult(Severity.Invalid, "Current handler cannot be past gen OT for transferred specimen.", CheckIdentifier.History);

            if (pkm.HT_Gender > 1)
                return new CheckResult(Severity.Invalid, $"HT Gender invalid {pkm.HT_Gender}.", CheckIdentifier.History);
            
            MysteryGift mg = EncounterMatch as MysteryGift;
            WC6 MatchedWC6 = EncounterMatch as WC6;
            WC7 MatchedWC7 = EncounterMatch as WC7;
            if (MatchedWC6?.OT.Length > 0) // Has Event OT -- null propagation yields false if MatchedWC6=null
            {
                if (pkm.OT_Friendship != PersonalTable.AO[MatchedWC6.Species].BaseFriendship)
                    return new CheckResult(Severity.Invalid, "Event OT Friendship does not match base friendship.", CheckIdentifier.History);
                if (pkm.OT_Affection != 0)
                    return new CheckResult(Severity.Invalid, "Event OT Affection should be zero.", CheckIdentifier.History);
                if (pkm.CurrentHandler != 1)
                    return new CheckResult(Severity.Invalid, "Current handler should not be Event OT.", CheckIdentifier.History);
            }
            else if (MatchedWC7?.OT.Length > 0) // Has Event OT -- null propagation yields false if MatchedWC7=null
            {
                if (pkm.OT_Friendship != PersonalTable.SM[MatchedWC7.Species].BaseFriendship)
                    return new CheckResult(Severity.Invalid, "Event OT Friendship does not match base friendship.", CheckIdentifier.History);
                if (pkm.OT_Affection != 0)
                    return new CheckResult(Severity.Invalid, "Event OT Affection should be zero.", CheckIdentifier.History);
                if (pkm.CurrentHandler != 1)
                    return new CheckResult(Severity.Invalid, "Current handler should not be Event OT.", CheckIdentifier.History);
            }
            else if (mg != null && mg.Format < 6 && pkm.Format >= 6)
            {
                if (pkm.OT_Affection != 0)
                    return new CheckResult(Severity.Invalid, "Event OT Affection should be zero.", CheckIdentifier.History);
                if (pkm.CurrentHandler != 1)
                    return new CheckResult(Severity.Invalid, "Current handler should not be Event OT.", CheckIdentifier.History);
            }
            
            // Geolocations
            var geo = new[]
            {
                pkm.Geo1_Country, pkm.Geo2_Country, pkm.Geo3_Country, pkm.Geo4_Country, pkm.Geo5_Country,
                pkm.Geo1_Region, pkm.Geo2_Region, pkm.Geo3_Region, pkm.Geo4_Region, pkm.Geo5_Region,
            };

            // Check sequential order (no zero gaps)
            bool geoEnd = false;
            for (int i = 0; i < 5; i++)
            {
                if (geoEnd && geo[i] != 0)
                    return new CheckResult(Severity.Invalid, "Geolocation Memories invalid.", CheckIdentifier.History);

                if (geo[i] != 0)
                    continue;
                if (geo[i + 5] != 0)
                    return new CheckResult(Severity.Invalid, "Geolocation Region without Country.", CheckIdentifier.History);
                geoEnd = true;
            }
            if (pkm.Format >= 7)
            {
                if (pkm.VC1)
                {
                    var hasGeo = geo.Any(d => d != 0);

                    if (!hasGeo)
                        return new CheckResult(Severity.Invalid, "Geolocation Memories should be present.", CheckIdentifier.History);
                }
                
                if (pkm.GenNumber >= 7 && pkm.CNTs.Any(stat => stat > 0))
                    return new CheckResult(Severity.Invalid, "Contest stats on SM origin should be zero.", CheckIdentifier.History);
                
                if (!pkm.WasEvent && pkm.HT_Name.Length == 0) // Is not Traded
                {
                    if (pkm.CurrentHandler != 0) // Badly edited; PKHeX doesn't trip this.
                        return new CheckResult(Severity.Invalid, "Untraded -- Current handler should not be the Handling Trainer.", CheckIdentifier.History);
                    if (pkm.HT_Friendship != 0)
                        return new CheckResult(Severity.Invalid, "Untraded -- Handling Trainer Friendship should be zero.", CheckIdentifier.History);
                    if (pkm.HT_Affection != 0)
                        return new CheckResult(Severity.Invalid, "Untraded -- Handling Trainer Affection should be zero.", CheckIdentifier.History);

                    // We know it is untraded (HT is empty), if it must be trade evolved flag it.
                    if (Legal.getHasTradeEvolved(pkm) // if evo chain requires a trade
                        && (EncounterMatch as EncounterSlot[])?.Any(slot => slot.Species == pkm.Species) != true // Wild Encounter
                        && (EncounterMatch as EncounterStatic)?.Species != pkm.Species) // Static Encounter
                    {
                        if (pkm.Species != 350) // Milotic
                            return new CheckResult(Severity.Invalid, "Untraded -- requires a trade evolution.", CheckIdentifier.History);
                        if (pkm.CNT_Beauty < 170) // Beauty Contest Stat Requirement
                            return new CheckResult(Severity.Invalid, "Untraded -- Beauty is not high enough for Level-up Evolution.", CheckIdentifier.History);
                        if (pkm.CurrentLevel == 1)
                            return new CheckResult(Severity.Invalid, "Untraded -- Beauty is high enough but still Level 1.", CheckIdentifier.History);
                    }
                }

                return new CheckResult(Severity.Valid, "S/M History Block is valid.", CheckIdentifier.History);
            }
            if (!pkm.WasEvent && !(pkm.WasLink && (EncounterMatch as EncounterLink)?.OT == false) && (pkm.HT_Name.Length == 0 || pkm.Geo1_Country == 0)) // Is not Traded
            {
                if (pkm.HT_Name.Length != 0)
                    return new CheckResult(Severity.Invalid, "GeoLocation Memory -- HT Name present but has no previous Country.", CheckIdentifier.History);
                if (pkm.Geo1_Country != 0)
                    return new CheckResult(Severity.Invalid, "GeoLocation Memory -- Previous country of residence but no Handling Trainer.", CheckIdentifier.History);
                if (pkm.HT_Memory != 0)
                    return new CheckResult(Severity.Invalid, "Memory -- Handling Trainer memory present but no Handling Trainer.", CheckIdentifier.History);
                if (pkm.CurrentHandler != 0) // Badly edited; PKHeX doesn't trip this.
                    return new CheckResult(Severity.Invalid, "Untraded -- Current handler should not be the Handling Trainer.", CheckIdentifier.History);
                if (pkm.HT_Friendship != 0)
                    return new CheckResult(Severity.Invalid, "Untraded -- Handling Trainer Friendship should be zero.", CheckIdentifier.History);
                if (pkm.HT_Affection != 0)
                    return new CheckResult(Severity.Invalid, "Untraded -- Handling Trainer Affection should be zero.", CheckIdentifier.History);
                if (pkm.XY && pkm.CNTs.Any(stat => stat > 0))
                    return new CheckResult(Severity.Invalid, "Untraded -- Contest stats on XY should be zero.", CheckIdentifier.History);

                // We know it is untraded (HT is empty), if it must be trade evolved flag it.
                if (Legal.getHasTradeEvolved(pkm) && (EncounterMatch as EncounterSlot[])?.Any(slot => slot.Species == pkm.Species) != true)
                {
                    if (pkm.Species != 350) // Milotic
                        return new CheckResult(Severity.Invalid, "Untraded -- requires a trade evolution.", CheckIdentifier.History);
                    if (pkm.CNT_Beauty < 170) // Beauty Contest Stat Requirement
                        return new CheckResult(Severity.Invalid, "Untraded -- Beauty is not high enough for Level-up Evolution.", CheckIdentifier.History);
                    if (pkm.CurrentLevel == 1)
                        return new CheckResult(Severity.Invalid, "Untraded -- Beauty is high enough but still Level 1.", CheckIdentifier.History);
                }
            }
            else // Is Traded
            {
                if (pkm.Format == 6 && pkm.HT_Memory == 0)
                    return new CheckResult(Severity.Invalid, "Memory -- missing Handling Trainer Memory.", CheckIdentifier.History);
            }

            // Memory ChecksResult
            if (pkm.IsEgg)
            {
                if (pkm.HT_Memory != 0)
                    return new CheckResult(Severity.Invalid, "Memory -- has Handling Trainer Memory.", CheckIdentifier.History);
                if (pkm.OT_Memory != 0)
                    return new CheckResult(Severity.Invalid, "Memory -- has Original Trainer Memory.", CheckIdentifier.History);
            }
            else if (EncounterType != typeof(WC6))
            {
                if (pkm.OT_Memory == 0 ^ !pkm.Gen6)
                    return new CheckResult(Severity.Invalid, "Memory -- missing Original Trainer Memory.", CheckIdentifier.History);
                if (pkm.GenNumber < 6 && pkm.OT_Affection != 0)
                    return new CheckResult(Severity.Invalid, "OT Affection should be zero.", CheckIdentifier.History);
            }
            // Unimplemented: Ingame Trade Memories

            return new CheckResult(Severity.Valid, "History is valid.", CheckIdentifier.History);
        }
        private CheckResult verifyCommonMemory(int handler)
        {
            int m = 0;
            int t = 0;
            string resultPrefix = "";
            switch (handler)
            {
                case 0:
                    m = pkm.OT_Memory;
                    t = pkm.OT_TextVar;
                    resultPrefix = "OT ";
                    break;
                case 1:
                    m = pkm.HT_Memory;
                    t = pkm.HT_TextVar;
                    resultPrefix = "HT ";
                    break;
            }
            int matchingMoveMemory = Array.IndexOf(Legal.MoveSpecificMemories[0], m);
            if (matchingMoveMemory != -1 && pkm.Species != 235 && !Legal.getCanLearnMachineMove(pkm, Legal.MoveSpecificMemories[1][matchingMoveMemory], 6))
                return new CheckResult(Severity.Invalid, resultPrefix + "Memory: Species cannot learn this move.", CheckIdentifier.Memory);

            if (m == 6 && !Legal.LocationsWithPKCenter[0].Contains(t))
                return new CheckResult(Severity.Invalid, resultPrefix + "Memory: Location doesn't have a Pokemon Center.", CheckIdentifier.Memory);

            if (m == 21) // {0} saw {2} carrying {1} on its back. {4} that {3}.
                if (!Legal.getCanLearnMachineMove(new PK6 {Species = t, EXP = PKX.getEXP(100, t)}, 19, 6))
                    return new CheckResult(Severity.Invalid, resultPrefix + "Memory: Argument Species cannot learn Fly.", CheckIdentifier.Memory);

            if ((m == 16 || m == 48) && (t == 0 || !Legal.getCanKnowMove(pkm, t, 6)))
                return new CheckResult(Severity.Invalid, resultPrefix + "Memory: Species cannot know this move.", CheckIdentifier.Memory);

            if (m == 49 && (t == 0 || !Legal.getCanRelearnMove(pkm, t, 6))) // {0} was able to remember {2} at {1}'s instruction. {4} that {3}.
                return new CheckResult(Severity.Invalid, resultPrefix + "Memory: Species cannot relearn this move.", CheckIdentifier.Memory);

            return new CheckResult(Severity.Valid, resultPrefix + "Memory is valid.", CheckIdentifier.Memory);
        }
        private void verifyOTMemory()
        {
            if (pkm.Format < 6)
                return;

            if (!History.Valid)
                return;

            if (pkm.GenNumber < 6)
            {
                if (pkm.OT_Memory != 0)
                    AddLine(Severity.Invalid, "Should not have an OT Memory.", CheckIdentifier.Memory);
                if (pkm.OT_Intensity != 0)
                    AddLine(Severity.Invalid, "Should not have an OT Memory Intensity value.", CheckIdentifier.Memory);
                if (pkm.OT_TextVar != 0)
                    AddLine(Severity.Invalid, "Should not have an OT Memory TextVar value.", CheckIdentifier.Memory);
                if (pkm.OT_Feeling != 0)
                    AddLine(Severity.Invalid, "Should not have an OT Memory Feeling value.", CheckIdentifier.Memory);
                return;
            }

            if (EncounterType == typeof(EncounterTrade))
            {
                // Undocumented, uncommon, and insignificant -- don't bother.
                AddLine(Severity.Valid, "OT Memory (Ingame Trade) is valid.", CheckIdentifier.Memory);
                return;
            }
            if (EncounterType == typeof(WC6))
            {
                WC6 MatchedWC6 = EncounterMatch as WC6;
                if (pkm.OT_Memory != MatchedWC6.OT_Memory)
                    AddLine(Severity.Invalid, "Event " + (MatchedWC6.OT_Memory == 0 ? "should not have an OT Memory" : "OT Memory should be index " + MatchedWC6.OT_Memory) + ".", CheckIdentifier.Memory);
                if (pkm.OT_Intensity != MatchedWC6.OT_Intensity)
                    AddLine(Severity.Invalid, "Event " + (MatchedWC6.OT_Intensity == 0 ? "should not have an OT Memory Intensity value" : "OT Memory Intensity should be index " + MatchedWC6.OT_Intensity) + ".", CheckIdentifier.Memory);
                if (pkm.OT_TextVar != MatchedWC6.OT_TextVar)
                    AddLine(Severity.Invalid, "Event " + (MatchedWC6.OT_TextVar == 0 ? "should not have an OT Memory TextVar value" : "OT Memory TextVar should be index " + MatchedWC6.OT_TextVar) + ".", CheckIdentifier.Memory);
                if (pkm.OT_Feeling != MatchedWC6.OT_Feeling)
                    AddLine(Severity.Invalid, "Event " + (MatchedWC6.OT_Feeling == 0 ? "should not have an OT Memory Feeling value" : "OT Memory Feeling should be index " + MatchedWC6.OT_Feeling) + ".", CheckIdentifier.Memory);
            }
            if (EncounterType == typeof(WC7))
            {
                WC7 MatchedWC7 = EncounterMatch as WC7;
                if (pkm.OT_Memory != MatchedWC7.OT_Memory)
                    AddLine(Severity.Invalid, "Event " + (MatchedWC7.OT_Memory == 0 ? "should not have an OT Memory" : "OT Memory should be index " + MatchedWC7.OT_Memory) + ".", CheckIdentifier.Memory);
                if (pkm.OT_Intensity != MatchedWC7.OT_Intensity)
                    AddLine(Severity.Invalid, "Event " + (MatchedWC7.OT_Intensity == 0 ? "should not have an OT Memory Intensity value" : "OT Memory Intensity should be index " + MatchedWC7.OT_Intensity) + ".", CheckIdentifier.Memory);
                if (pkm.OT_TextVar != MatchedWC7.OT_TextVar)
                    AddLine(Severity.Invalid, "Event " + (MatchedWC7.OT_TextVar == 0 ? "should not have an OT Memory TextVar value" : "OT Memory TextVar should be index " + MatchedWC7.OT_TextVar) + ".", CheckIdentifier.Memory);
                if (pkm.OT_Feeling != MatchedWC7.OT_Feeling)
                    AddLine(Severity.Invalid, "Event " + (MatchedWC7.OT_Feeling == 0 ? "should not have an OT Memory Feeling value" : "OT Memory Feeling should be index " + MatchedWC7.OT_Feeling) + ".", CheckIdentifier.Memory);
            }
            else if (pkm.GenNumber == 7)
            {
                if (pkm.OT_Memory != 0)
                    AddLine(Severity.Invalid, "Should not have an OT Memory.", CheckIdentifier.Memory);
                if (pkm.OT_Intensity != 0)
                    AddLine(Severity.Invalid, "Should not have an OT Memory Intensity value.", CheckIdentifier.Memory);
                if (pkm.OT_TextVar != 0)
                    AddLine(Severity.Invalid, "Should not have an OT Memory TextVar value.", CheckIdentifier.Memory);
                if (pkm.OT_Feeling != 0)
                    AddLine(Severity.Invalid, "Should not have an OT Memory Feeling value.", CheckIdentifier.Memory);
                return;
            }

            switch (pkm.OT_Memory)
            {
                case 2: // {0} hatched from an Egg and saw {1} for the first time at... {2}. {4} that {3}.
                    if (!pkm.WasEgg && pkm.Egg_Location != 60004)
                        AddLine(Severity.Invalid, "OT Memory: OT did not hatch this.", CheckIdentifier.Memory);
                    break;

                case 4: // {0} became {1}’s friend when it arrived via Link Trade at... {2}. {4} that {3}.
                    AddLine(Severity.Invalid, "OT Memory: Link Trade is not a valid first memory.", CheckIdentifier.Memory);
                    return;

                case 6: // {0} went to the Pokémon Center in {2} with {1} and had its tired body healed there. {4} that {3}.
                    int matchingOriginGame = Array.IndexOf(Legal.LocationsWithPKCenter[0], pkm.OT_TextVar);
                    if (matchingOriginGame != -1)
                    {
                        int gameID = Legal.LocationsWithPKCenter[1][matchingOriginGame];
                        if (pkm.XY && gameID != 0 || pkm.AO && gameID != 1)
                            AddLine(Severity.Invalid, "OT Memory: Location doesn't exist on Origin Game region.", CheckIdentifier.Memory);
                    }
                    AddLine(verifyCommonMemory(0));
                    return;

                case 14:
                    if (!Legal.getCanBeCaptured(pkm.OT_TextVar, pkm.GenNumber, (GameVersion)pkm.Version))
                        AddLine(Severity.Invalid, "OT Memory: Captured Species can not be captured in game.", CheckIdentifier.Memory);
                    else
                        AddLine(Severity.Valid, "OT Memory: Captured Species can be captured in game.", CheckIdentifier.Memory);
                    return;
            }
            if (pkm.XY && Legal.Memory_NotXY.Contains(pkm.OT_Memory))
                AddLine(Severity.Invalid, "OT Memory: OR/AS exclusive memory on X/Y origin.", CheckIdentifier.Memory);
            if (pkm.AO && Legal.Memory_NotAO.Contains(pkm.OT_Memory))
                AddLine(Severity.Invalid, "OT Memory: X/Y exclusive memory on OR/AS origin.", CheckIdentifier.Memory);

            AddLine(verifyCommonMemory(0));
        }
        private void verifyHTMemory()
        {
            if (pkm.Format < 6)
                return;

            if (!History.Valid)
                return;

            if (pkm.GenNumber == 7 || pkm.GenNumber == 1)
            {
                bool check = pkm.VC1 || pkm.HT_Memory != 0;
                if (!check)
                    return;

                if (pkm.HT_Memory != 4)
                    AddLine(Severity.Invalid, "Should have a Link Trade HT Memory.", CheckIdentifier.Memory);
                if (pkm.HT_TextVar != 0)
                    AddLine(Severity.Invalid, "Should have a HT Memory TextVar value (somewhere).", CheckIdentifier.Memory);
                if (pkm.HT_Intensity != 1)
                    AddLine(Severity.Invalid, "Should have a HT Memory Intensity value (1st).", CheckIdentifier.Memory);
                if (pkm.HT_Feeling > 10)
                    AddLine(Severity.Invalid, "Should have a HT Memory Feeling value 0-9.", CheckIdentifier.Memory);
                return;
            }

            switch (pkm.HT_Memory)
            {
                case 0:
                    if (string.IsNullOrEmpty(pkm.HT_Name))
                        return;
                    AddLine(Severity.Invalid, "HT Memory is missing.", CheckIdentifier.Memory); return;
                case 1: // {0} met {1} at... {2}. {1} threw a Poké Ball at it, and they started to travel together. {4} that {3}.
                    AddLine(Severity.Invalid, "HT Memory: Handling Trainer did not capture this.", CheckIdentifier.Memory); return;

                case 2: // {0} hatched from an Egg and saw {1} for the first time at... {2}. {4} that {3}.
                    AddLine(Severity.Invalid, "HT Memory: Handling Trainer did not hatch this.", CheckIdentifier.Memory); return;

                case 14:
                    if (!Legal.getCanBeCaptured(pkm.HT_TextVar, pkm.GenNumber))
                        AddLine(Severity.Invalid, "HT Memory: Captured Species can not be captured in game.", CheckIdentifier.Memory);
                    else
                        AddLine(Severity.Valid, "HT Memory: Captured Species can be captured in game.", CheckIdentifier.Memory);
                    return;
            }
            AddLine(verifyCommonMemory(1));
        }
        private void verifyRegion()
        {
            if (pkm.Format < 6)
                return;

            bool pass;
            switch (pkm.ConsoleRegion)
            {
                case 0: // Japan
                    pass = pkm.Country == 1;
                    break;
                case 1: // Americas
                    pass = 8 <= pkm.Country && pkm.Country <= 52 || new[] {153, 156, 168, 174, 186}.Contains(pkm.Country);
                    break;
                case 2: // Europe
                    pass = 64 <= pkm.Country && pkm.Country <= 127 || new[] {169, 184, 185}.Contains(pkm.Country);
                    break;
                case 4: // China
                    pass = pkm.Country == 144 || pkm.Country == 160;
                    break;
                case 5: // Korea
                    pass = pkm.Country == 136;
                    break;
                case 6: // Taiwan
                    pass = pkm.Country == 128;
                    break;
                default:
                    AddLine(new CheckResult(Severity.Invalid, "Invalid Console Region.", CheckIdentifier.Geography));
                    return;
            }

            if (!pass)
                AddLine(Severity.Invalid, "Geolocation: Country is not in 3DS region.", CheckIdentifier.Geography);
            else
                AddLine(Severity.Valid, "Geolocation: Country is in 3DS region.", CheckIdentifier.Geography);
        }
        private void verifyForm()
        {
            if (!Encounter.Valid)
                return;

            if (pkm.Format < 4)
                return;

            if (pkm.AltForm > pkm.PersonalInfo.FormeCount)
            {
                bool valid = false;
                int species = pkm.Species;
                if (species == 201) // Unown
                {
                    if (pkm.GenNumber == 2 && pkm.AltForm < 26) // A-Z
                        valid = true;
                    else if (pkm.GenNumber >= 3 && pkm.AltForm >= 28) // A-Z?!
                        valid = true;
                }
                if (species == 414 && pkm.AltForm < 3) // Wormadam base form kept
                        valid = true;

                if ((species == 664 || species == 665) && pkm.AltForm < 18) // Vivillon Pre-evolutions
                    valid = true;

                if (!valid) // ignore list
                { AddLine(Severity.Invalid, $"Form Count is out of range. Expected <= {pkm.PersonalInfo.FormeCount}, got {pkm.AltForm}", CheckIdentifier.Form); return; }
            }

            switch (pkm.Species)
            {
                case 25: // Pikachu
                    if (pkm.Format == 6 && pkm.AltForm != 0 ^ EncounterType == typeof(EncounterStatic))
                    {
                        string msg = EncounterType == typeof (EncounterStatic)
                            ? "Cosplay Pikachu cannot have the default form."
                            : "Only Cosplay Pikachu can have this form.";
                        AddLine(Severity.Invalid, msg, CheckIdentifier.Form);
                        return;
                    }
                    if (pkm.Format == 7 && pkm.AltForm != 0 ^ EncounterIsMysteryGift)
                    {
                        var gift = EncounterMatch as WC7;
                        if (gift != null && gift.Form != pkm.AltForm)
                        {
                            AddLine(Severity.Invalid, "Event Pikachu cannot have the default form.", CheckIdentifier.Form);
                            return;
                        }
                    }
                    break;
                case 487: // Giratina
                    if (pkm.AltForm == 1 ^ pkm.HeldItem == 112) // Origin form only with Griseous Orb
                    {
                        AddLine(Severity.Invalid, "Held item does not match Form.", CheckIdentifier.Form);
                        return;
                    }
                    break;
                case 493: // Arceus
                    {
                        int item = pkm.HeldItem;
                        int form = 0;
                        if ((298 <= item && item <= 313) || item == 644)
                            form = Array.IndexOf(Legal.Arceus_Plate, item) + 1;
                        else if (777 <= item && item <= 793)
                            form = Array.IndexOf(Legal.Arceus_ZCrystal, item) + 1;
                        if (form != pkm.AltForm)
                            AddLine(Severity.Invalid, "Held item does not match Form.", CheckIdentifier.Form);
                        else if (form != 0)
                            AddLine(Severity.Valid, "Held item matches Form.", CheckIdentifier.Form);
                    }
                    break;
                case 649: // Genesect
                    {
                        int item = pkm.HeldItem;
                        int form = 0;
                        if (116 <= item && item <= 119)
                            form = item - 115;

                        if (form != pkm.AltForm)
                            AddLine(Severity.Invalid, "Held item does not match Form.", CheckIdentifier.Form);
                        else
                            AddLine(Severity.Valid, "Held item matches Form.", CheckIdentifier.Form);
                    }
                    break;
                case 658: // Greninja
                    if (pkm.AltForm > 1) // Ash Battle Bond active
                    {
                        AddLine(Severity.Invalid, "Form cannot exist outside of a battle.", CheckIdentifier.Form);
                        return;
                    }
                    break;
                case 664: // Scatterbug
                case 665: // Spewpa
                    if (pkm.AltForm > 17) // Fancy & Pokéball
                    {
                        AddLine(Severity.Invalid, "Event Vivillon pattern on pre-evolution.", CheckIdentifier.Form);
                        return;
                    }
                    break;
                case 666: // Vivillon
                    if (pkm.AltForm > 17) // Fancy & Pokéball
                    {
                        if (!EncounterIsMysteryGift)
                            AddLine(Severity.Invalid, "Invalid Vivillon pattern.", CheckIdentifier.Form);
                        else
                            AddLine(Severity.Valid, "Valid Vivillon pattern.", CheckIdentifier.Form);

                        return;
                    }
                    break;
                case 670: // Floette
                    if (pkm.AltForm == 5) // Eternal Flower -- Never Released
                    {
                        if (!EncounterIsMysteryGift)
                            AddLine(Severity.Invalid, "Invalid Eternal Flower encounter.", CheckIdentifier.Form);
                        else
                            AddLine(Severity.Valid, "Valid Eternal Flower encounter.", CheckIdentifier.Form);

                        return;
                    }
                    break;
                case 718: // Zygarde
                    if (pkm.AltForm >= 4)
                    {
                        AddLine(Severity.Invalid, "Form cannot exist outside of a battle.", CheckIdentifier.Form);
                        return;
                    }
                    break;
                case 773: // Silvally
                    {
                        int item = pkm.HeldItem;
                        int form = 0;
                        if ((904 <= item && item <= 920) || item == 644)
                            form = item - 903;
                        if (form != pkm.AltForm)
                            AddLine(Severity.Invalid, "Held item does not match Form.", CheckIdentifier.Form);
                        else if (form != 0)
                            AddLine(Severity.Valid, "Held item matches Form.", CheckIdentifier.Form);
                        break;
                    }
                case 774: // Minior
                    if (pkm.AltForm < 7)
                    {
                        AddLine(Severity.Invalid, "Form cannot exist outside of a battle.", CheckIdentifier.Form);
                        return;
                    }
                    break;

                // Party Only Forms
                case 492: // Shaymin
                case 676: // Furfrou
                case 720: // Hoopa
                    if (pkm.AltForm != 0 && pkm.Box > -1) // has form but stored in box
                    {
                        AddLine(Severity.Invalid, "Form cannot exist outside of Party.", CheckIdentifier.Form);
                        return;
                    }
                    break;
            }

            if (pkm.Format >= 7 && pkm.GenNumber < 7 && pkm.AltForm != 0)
            {
                if (pkm.Species == 25 || Legal.AlolanOriginForms.Contains(pkm.Species))
                { AddLine(Severity.Invalid, "Form cannot be obtained for pre-Alola generation games.", CheckIdentifier.Form); return; }
            }
            if (pkm.AltForm > 0 && new[] {Legal.BattleForms, Legal.BattleMegas, Legal.BattlePrimals}.Any(arr => arr.Contains(pkm.Species)))
            { AddLine(Severity.Invalid, "Form cannot exist outside of a battle.", CheckIdentifier.Form); return; }

            AddLine(Severity.Valid, "Form is Valid.", CheckIdentifier.Form);
        }
        private void verifyMisc()
        {
            if (pkm.Format == 7 && ((PK7)pkm).PelagoEventStatus != 0)
            {
                // TODO: Figure out what PelagoEventStati are legal.
            }

            if (pkm.IsEgg)
            {
                if (new[] {pkm.Move1_PPUps, pkm.Move2_PPUps, pkm.Move3_PPUps, pkm.Move4_PPUps}.Any(ppup => ppup > 0))
                { AddLine(Severity.Invalid, "Cannot apply PP Ups to an Egg.", CheckIdentifier.Misc); return; }
                if (pkm.CNTs.Any(stat => stat > 0))
                { AddLine(Severity.Invalid, "Cannot increase Contest Stats of an Egg.", CheckIdentifier.Misc); return; }
            }

            if (Encounter.Valid)
            {
                if (EncounterIsMysteryGift)
                {
                    if (pkm.FatefulEncounter)
                        AddLine(Severity.Valid, "Mystery Gift Fateful Encounter.", CheckIdentifier.Fateful);
                    else
                        AddLine(Severity.Invalid, "Mystery Gift Fateful Encounter flag missing.", CheckIdentifier.Fateful);
                    return;
                }
                if (EncounterType == typeof (EncounterStatic))
                {
                    var enc = EncounterMatch as EncounterStatic;
                    if (enc.Fateful)
                    {
                        if (pkm.FatefulEncounter)
                            AddLine(Severity.Valid, "Special ingame Fateful Encounter.", CheckIdentifier.Fateful);
                        else
                            AddLine(Severity.Invalid, "Special ingame Fateful Encounter flag missing.", CheckIdentifier.Fateful);
                    }
                    else if (pkm.FatefulEncounter)
                        AddLine(Severity.Invalid, "Fateful Encounter should not be checked.", CheckIdentifier.Fateful);
                    return;
                }
                if (pkm.FatefulEncounter)
                    AddLine(Severity.Invalid, "Fateful Encounter should not be checked.", CheckIdentifier.Fateful);
            }
        }
        private void verifyVersionEvolution()
        {
            if (pkm.Format < 7)
                return;

            // No point using the evolution tree. Just handle certain species.
            switch (pkm.Species)
            {
                case 745: // Lycanroc
                    if (!pkm.WasEgg)
                        break;

                    if (pkm.AltForm == 0 && pkm.Version == 31 // Moon
                        || pkm.AltForm == 1 && pkm.Version == 30) // Sun
                        if (pkm.IsUntraded)
                            AddLine(Severity.Invalid, "Version Specific evolution requires a trade to opposite version. A Handling Trainer is required.", CheckIdentifier.Evolution);
                    break;

                case 791: // Solgaleo
                    if (pkm.Version == 31 && pkm.IsUntraded)
                    {
                        if (EncounterIsMysteryGift && (EncounterMatch as MysteryGift).Species == pkm.Species) // Gifted via Mystery Gift
                            break;
                        AddLine(Severity.Invalid, "Version Specific evolution requires a trade to opposite version. A Handling Trainer is required.", CheckIdentifier.Evolution);
                    }
                    break;
                case 792: // Lunala
                    if (pkm.Version == 30 && pkm.IsUntraded)
                    {
                        if (EncounterIsMysteryGift && (EncounterMatch as MysteryGift).Species == pkm.Species) // Gifted via Mystery Gift
                            break;
                        AddLine(Severity.Invalid, "Version Specific evolution requires a trade to opposite version. A Handling Trainer is required.", CheckIdentifier.Evolution);
                    }
                    break;
            }
        }

        private CheckResult[] verifyMoves(GameVersion game = GameVersion.Any)
        {
            int[] Moves = pkm.Moves;
            CheckResult[] res = new CheckResult[4];
            for (int i = 0; i < 4; i++)
                res[i] = new CheckResult(CheckIdentifier.Move);
            
            var validLevelMoves = Legal.getValidMoves(pkm, EvoChainsAllGens, Tutor: false, Machine: false).ToArray();
            var validTMHM = Legal.getValidMoves(pkm, EvoChainsAllGens, LVL: false, Tutor: false, MoveReminder: false).ToArray();
            var validTutor = Legal.getValidMoves(pkm, EvoChainsAllGens, LVL: false, Machine: false, MoveReminder: false).ToArray();
            if (pkm.Species == 235) // Smeargle
            {
                for (int i = 0; i < 4; i++)
                    res[i] = Legal.InvalidSketch.Contains(Moves[i])
                        ? new CheckResult(Severity.Invalid, "Invalid Sketch move.", CheckIdentifier.Move)
                        : new CheckResult(CheckIdentifier.Move);
            }
            else if (EventGiftMatch?.Count > 1) // Multiple possible Mystery Gifts matched
            {
                int[] RelearnMoves = pkm.RelearnMoves;
                foreach (MysteryGift mg in EventGiftMatch)
                {
                    int[] SpecialMoves = mg.Moves;
                    res = parseMoves(Moves, validLevelMoves, RelearnMoves, validTMHM, validTutor, SpecialMoves, new int[0]);
                    if (res.Any(r => !r.Valid))
                        continue;

                    EncounterMatch = mg;
                    RelearnBase = mg.RelearnMoves;
                    break;
                }
            }
            else
            {
                int[] EggMoves = pkm.WasEgg ? Legal.getEggMoves(pkm, game).ToArray() : new int[0];
                int[] RelearnMoves = pkm.RelearnMoves;
                int[] SpecialMoves = (EncounterMatch as MysteryGift)?.Moves ??
                                     (EncounterMatch as EncounterStatic)?.Moves ??
                                     (EncounterMatch as EncounterTrade)?.Moves ??
                                     new int[0];

                res = parseMoves(Moves, validLevelMoves, RelearnMoves, validTMHM, validTutor, SpecialMoves, EggMoves);
            }
            if (Moves[0] == 0) // None
                res[0] = new CheckResult(Severity.Invalid, "Invalid Move.", CheckIdentifier.Move);

            if (pkm.Species == 647) // Keldeo
                if (pkm.AltForm == 1 ^ pkm.Moves.Contains(548))
                    res[Math.Max(Array.IndexOf(pkm.Moves, 548), 0)] = new CheckResult(Severity.Invalid, "Secret Sword / Resolute Keldeo Mismatch.", CheckIdentifier.Move);

            // Duplicate Moves Check
            for (int i = 0; i < 4; i++)
                if (Moves.Count(m => m != 0 && m == Moves[i]) > 1)
                    res[i] = new CheckResult(Severity.Invalid, "Duplicate Move.", CheckIdentifier.Move);

            return res;
        }
        private static CheckResult[] parseMoves(int[] moves, int[] learn, int[] relearn, int[] tmhm, int[] tutor, int[] special, int[] egg)
        {
            CheckResult[] res = new CheckResult[4];
            for (int i = 0; i < 4; i++)
            {
                if (moves[i] == 0)
                    res[i] = new CheckResult(Severity.Valid, "Empty", CheckIdentifier.Move);
                else if (learn.Contains(moves[i]))
                    res[i] = new CheckResult(Severity.Valid, "Learned by Level-up.", CheckIdentifier.Move);
                else if (egg.Contains(moves[i]))
                    res[i] = new CheckResult(Severity.Valid, "Egg Move.", CheckIdentifier.Move) { Flag = true };
                else if (relearn.Contains(moves[i]))
                    res[i] = new CheckResult(Severity.Valid, "Relearnable Move.", CheckIdentifier.Move) { Flag = true };
                else if (tmhm.Contains(moves[i]))
                    res[i] = new CheckResult(Severity.Valid, "Learned by TM/HM.", CheckIdentifier.Move);
                else if (tutor.Contains(moves[i]))
                    res[i] = new CheckResult(Severity.Valid, "Learned by Move Tutor.", CheckIdentifier.Move);
                else if (special.Contains(moves[i]))
                    res[i] = new CheckResult(Severity.Valid, "Special Non-Relearn Move.", CheckIdentifier.Move);
                else
                    res[i] = new CheckResult(Severity.Invalid, "Invalid Move.", CheckIdentifier.Move);
            }
            return res;
        }
        private CheckResult[] verifyRelearn()
        {
            RelearnBase = null;
            CheckResult[] res = new CheckResult[4];
            
            int[] Moves = pkm.RelearnMoves;
            if (pkm.GenNumber < 6 || pkm.VC1)
                goto noRelearn;

            if (pkm.WasLink)
            {
                var Link = Legal.getValidLinkGifts(pkm);
                if (Link == null)
                {
                    for (int i = 0; i < 4; i++)
                        res[i] = new CheckResult(CheckIdentifier.RelearnMove);
                    return res;
                }
                EncounterMatch = Link;

                int[] moves = ((EncounterLink)EncounterMatch).RelearnMoves;
                RelearnBase = moves;
                for (int i = 0; i < 4; i++)
                    res[i] = moves[i] != Moves[i]
                        ? new CheckResult(Severity.Invalid, $"Expected: {movelist[moves[i]]}.", CheckIdentifier.RelearnMove)
                        : new CheckResult(CheckIdentifier.RelearnMove);
                return res;
            }
            if (pkm.WasEvent || pkm.WasEventEgg)
            {
                // Get WC6's that match
                EventGiftMatch = new List<MysteryGift>(Legal.getValidGifts(pkm));
                foreach (MysteryGift mg in EventGiftMatch.ToArray())
                {
                    int[] moves = mg.RelearnMoves;
                    for (int i = 0; i < 4; i++)
                        res[i] = moves[i] != Moves[i]
                            ? new CheckResult(Severity.Invalid, $"Expected: {movelist[moves[i]]}.", CheckIdentifier.RelearnMove)
                            : new CheckResult(CheckIdentifier.RelearnMove);
                    if (res.Any(r => !r.Valid))
                        EventGiftMatch.Remove(mg);
                }
                if (EventGiftMatch.Count > 1)
                    return res;
                if (EventGiftMatch.Count == 1)
                { EncounterMatch = EventGiftMatch[0]; RelearnBase = EventGiftMatch[0].RelearnMoves; return res; }

                EncounterMatch = EncounterType = null;
                goto noRelearn; // No WC match
            }

            if (pkm.WasEgg && !Legal.NoHatchFromEgg.Contains(pkm.Species))
            {
                GameVersion[] Games = {};
                switch (pkm.GenNumber)
                {
                    case 6:
                        Games = new[] {GameVersion.XY, GameVersion.ORAS};
                        break;
                    case 7:
                        Games = new[] {GameVersion.SM};
                        break;
                }

                bool checkAllGames = pkm.WasTradedEgg;
                bool splitBreed = Legal.SplitBreed.Contains(pkm.Species);

                int iterate = (checkAllGames ? Games.Length : 1) * (splitBreed ? 2 : 1);
                for (int i = 0; i < iterate; i++)
                {
                    int gameSource = !checkAllGames ? -1 : i % iterate / (splitBreed ? 2 : 1);
                    int skipOption = splitBreed && iterate / 2 <= i ? 1 : 0;
                    GameVersion ver = gameSource == -1 ? GameVersion.Any : Games[gameSource];

                    // Obtain level1 moves
                    List<int> baseMoves = new List<int>(Legal.getBaseEggMoves(pkm, skipOption, ver));
                    int baseCt = baseMoves.Count;
                    if (baseCt > 4) baseCt = 4;

                    // Obtain Nonstandard moves
                    var relearnMoves = Legal.getValidRelearn(pkm, skipOption).ToArray();
                    var relearn = pkm.RelearnMoves.Where(move => move != 0 
                        && (!baseMoves.Contains(move) || relearnMoves.Contains(move))
                        ).ToArray();
                    int relearnCt = relearn.Length;

                    // Get Move Window
                    List<int> window = new List<int>(baseMoves);
                    window.AddRange(relearn);
                    int[] moves = window.Skip(baseCt + relearnCt - 4).Take(4).ToArray();
                    Array.Resize(ref moves, 4);

                    int reqBase;
                    int unique = baseMoves.Concat(relearn).Distinct().Count();
                    if (relearnCt == 4)
                        reqBase = 0;
                    else if (baseCt + relearnCt > 4)
                        reqBase = 4 - relearnCt;
                    else
                        reqBase = baseCt;

                    if (pkm.RelearnMoves.Where(m=>m != 0).Count() < Math.Min(4, baseMoves.Count))
                        reqBase = Math.Min(4, unique);

                    // Movepool finalized! Check validity.
                    
                    int[] rl = pkm.RelearnMoves;
                    string em = string.Join(", ", baseMoves.Select(r => r >= movelist.Length ? "ERROR" : movelist[r]));
                    RelearnBase = baseMoves.ToArray();
                    // Base Egg Move
                    for (int j = 0; j < reqBase; j++)
                    {
                        if (baseMoves.Contains(rl[j]))
                            res[j] = new CheckResult(Severity.Valid, "Base egg move.", CheckIdentifier.RelearnMove);
                        else
                        {
                            res[j] = new CheckResult(Severity.Invalid, "Base egg move missing.", CheckIdentifier.RelearnMove);
                            for (int f = j+1; f < reqBase; f++)
                                res[f] = new CheckResult(Severity.Invalid, "Base egg move missing.", CheckIdentifier.RelearnMove);
                            res[reqBase-1].Comment += $"{Environment.NewLine}Expected the following Relearn Moves: {em}.";
                            break;
                        }
                    }

                    // Non-Base
                    if (Legal.LightBall.Contains(pkm.Species))
                        relearnMoves = relearnMoves.Concat(new[] { 344 }).ToArray();
                    for (int j = reqBase; j < 4; j++)
                        res[j] = !relearnMoves.Contains(rl[j])
                            ? new CheckResult(Severity.Invalid, "Not an expected relearn move.", CheckIdentifier.RelearnMove)
                            : new CheckResult(Severity.Valid, rl[j] == 0 ? "Empty" : "Relearn move.", CheckIdentifier.RelearnMove);

                    if (res.All(r => r.Valid))
                        break;
                }
                
                // Duplicate Moves Check
                for (int i = 0; i < 4; i++)
                    if (Moves.Count(m => m != 0 && m == Moves[i]) > 1)
                        res[i] = new CheckResult(Severity.Invalid, "Duplicate Relearn Move.", CheckIdentifier.RelearnMove);

                return res;
            }
            if (Moves[0] != 0) // DexNav only?
            {
                // Check DexNav
                if (!Legal.getDexNavValid(pkm))
                    goto noRelearn;

                res[0] = !Legal.getValidRelearn(pkm, 0).Contains(Moves[0])
                        ? new CheckResult(Severity.Invalid, "Not an expected DexNav move.", CheckIdentifier.RelearnMove)
                        : new CheckResult(CheckIdentifier.RelearnMove);
                for (int i = 1; i < 4; i++)
                    res[i] = Moves[i] != 0
                        ? new CheckResult(Severity.Invalid, "Expected no Relearn Move in slot.", CheckIdentifier.RelearnMove)
                        : new CheckResult(CheckIdentifier.RelearnMove);

                if (res[0].Valid)
                    RelearnBase = new[] { Moves[0], 0, 0, 0 };
                return res;
            }

            // Should have no relearn moves.
            noRelearn:
            for (int i = 0; i < 4; i++)
                res[i] = Moves[i] != 0
                    ? new CheckResult(Severity.Invalid, "Expected no Relearn Moves.", CheckIdentifier.RelearnMove)
                    : new CheckResult(CheckIdentifier.RelearnMove);
            return res;
        }
        private CheckResult verifyEggMoves()
        {
            if (!pkm.WasEgg || vMoves.All(m => m.Valid))
                return new CheckResult(CheckIdentifier.Egg);

            // todo: egg move breeding legality
            switch (pkm.GenNumber)
            {
                case 1:
                case 2:
                    // Check Both Egg Moves -- egg moves are initially checked with no game as the base.
                    foreach (var game in new[] {GameVersion.GS, GameVersion.C})
                    {
                        vMoves = verifyMoves(game);
                        if (vMoves.Any(m => !m.Valid))
                            continue;

                        // todo: check compatibility of parents (chain wise)
                        return new CheckResult(Severity.Valid, $"Egg Moves sourced from {game}.", CheckIdentifier.Egg);
                    }
                    break;

                case 3:
                    return new CheckResult(Severity.NotImplemented, "Egg Move set check unimplemented.", CheckIdentifier.Egg);

                case 4:
                    return new CheckResult(Severity.NotImplemented, "Egg Move set check unimplemented.", CheckIdentifier.Egg);
            }
            return new CheckResult(CheckIdentifier.Egg);
        }

        public static string[] movelist = Util.getMovesList("en");
        private static readonly string[] EventRibName =
        {
            "Country", "National", "Earth", "World", "Classic",
            "Premier", "Event", "Birthday", "Special", "Souvenir",
            "Wishing", "Battle Champ", "Regional Champ", "National Champ", "World Champ"
        };
    }
}
