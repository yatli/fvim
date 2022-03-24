(*
    wcwidth implementation ported from:
    https://github.com/termux/wcwidth
*)
module FVim.wcwidth

open def
open common
open log

// From https://github.com/jquast/wcwidth/blob/master/wcwidth/table_zero.py
// at commit 0d7de112202cc8b2ebe9232ff4a5c954f19d561a (2016-07-02):
let ZeroWidth = [| 
    (0x0300u, 0x036fu)  // Combining Grave Accent  ..Combining Latin Small Le
    (0x0483u, 0x0489u)  // Combining Cyrillic Titlo..Combining Cyrillic Milli
    (0x0591u, 0x05bdu)  // Hebrew Accent Etnahta   ..Hebrew Point Meteg
    (0x05bfu, 0x05bfu)  // Hebrew Point Rafe       ..Hebrew Point Rafe
    (0x05c1u, 0x05c2u)  // Hebrew Point Shin Dot   ..Hebrew Point Sin Dot
    (0x05c4u, 0x05c5u)  // Hebrew Mark Upper Dot   ..Hebrew Mark Lower Dot
    (0x05c7u, 0x05c7u)  // Hebrew Point Qamats Qata..Hebrew Point Qamats Qata
    (0x0610u, 0x061au)  // Arabic Sign Sallallahou ..Arabic Small Kasra
    (0x064bu, 0x065fu)  // Arabic Fathatan         ..Arabic Wavy Hamza Below
    (0x0670u, 0x0670u)  // Arabic Letter Superscrip..Arabic Letter Superscrip
    (0x06d6u, 0x06dcu)  // Arabic Small High Ligatu..Arabic Small High Seen
    (0x06dfu, 0x06e4u)  // Arabic Small High Rounde..Arabic Small High Madda
    (0x06e7u, 0x06e8u)  // Arabic Small High Yeh   ..Arabic Small High Noon
    (0x06eau, 0x06edu)  // Arabic Empty Centre Low ..Arabic Small Low Meem
    (0x0711u, 0x0711u)  // Syriac Letter Superscrip..Syriac Letter Superscrip
    (0x0730u, 0x074au)  // Syriac Pthaha Above     ..Syriac Barrekh
    (0x07a6u, 0x07b0u)  // Thaana Abafili          ..Thaana Sukun
    (0x07ebu, 0x07f3u)  // Nko Combining Sh||t High..Nko Combining Double Dot
    (0x0816u, 0x0819u)  // Samaritan Mark In       ..Samaritan Mark Dagesh
    (0x081bu, 0x0823u)  // Samaritan Mark Epentheti..Samaritan Vowel Sign A
    (0x0825u, 0x0827u)  // Samaritan Vowel Sign Sho..Samaritan Vowel Sign U
    (0x0829u, 0x082du)  // Samaritan Vowel Sign Lon..Samaritan Mark Nequdaa
    (0x0859u, 0x085bu)  // Mandaic Affrication Mark..Mandaic Gemination Mark
    (0x08d4u, 0x08e1u)  // (nil)                   ..
    (0x08e3u, 0x0902u)  // Arabic Turned Damma Belo..Devanagari Sign Anusvara
    (0x093au, 0x093au)  // Devanagari Vowel Sign Oe..Devanagari Vowel Sign Oe
    (0x093cu, 0x093cu)  // Devanagari Sign Nukta   ..Devanagari Sign Nukta
    (0x0941u, 0x0948u)  // Devanagari Vowel Sign U ..Devanagari Vowel Sign Ai
    (0x094du, 0x094du)  // Devanagari Sign Virama  ..Devanagari Sign Virama
    (0x0951u, 0x0957u)  // Devanagari Stress Sign U..Devanagari Vowel Sign Uu
    (0x0962u, 0x0963u)  // Devanagari Vowel Sign Vo..Devanagari Vowel Sign Vo
    (0x0981u, 0x0981u)  // Bengali Sign Candrabindu..Bengali Sign Candrabindu
    (0x09bcu, 0x09bcu)  // Bengali Sign Nukta      ..Bengali Sign Nukta
    (0x09c1u, 0x09c4u)  // Bengali Vowel Sign U    ..Bengali Vowel Sign Vocal
    (0x09cdu, 0x09cdu)  // Bengali Sign Virama     ..Bengali Sign Virama
    (0x09e2u, 0x09e3u)  // Bengali Vowel Sign Vocal..Bengali Vowel Sign Vocal
    (0x0a01u, 0x0a02u)  // Gurmukhi Sign Adak Bindi..Gurmukhi Sign Bindi
    (0x0a3cu, 0x0a3cu)  // Gurmukhi Sign Nukta     ..Gurmukhi Sign Nukta
    (0x0a41u, 0x0a42u)  // Gurmukhi Vowel Sign U   ..Gurmukhi Vowel Sign Uu
    (0x0a47u, 0x0a48u)  // Gurmukhi Vowel Sign Ee  ..Gurmukhi Vowel Sign Ai
    (0x0a4bu, 0x0a4du)  // Gurmukhi Vowel Sign Oo  ..Gurmukhi Sign Virama
    (0x0a51u, 0x0a51u)  // Gurmukhi Sign Udaat     ..Gurmukhi Sign Udaat
    (0x0a70u, 0x0a71u)  // Gurmukhi Tippi          ..Gurmukhi Addak
    (0x0a75u, 0x0a75u)  // Gurmukhi Sign Yakash    ..Gurmukhi Sign Yakash
    (0x0a81u, 0x0a82u)  // Gujarati Sign Candrabind..Gujarati Sign Anusvara
    (0x0abcu, 0x0abcu)  // Gujarati Sign Nukta     ..Gujarati Sign Nukta
    (0x0ac1u, 0x0ac5u)  // Gujarati Vowel Sign U   ..Gujarati Vowel Sign Cand
    (0x0ac7u, 0x0ac8u)  // Gujarati Vowel Sign E   ..Gujarati Vowel Sign Ai
    (0x0acdu, 0x0acdu)  // Gujarati Sign Virama    ..Gujarati Sign Virama
    (0x0ae2u, 0x0ae3u)  // Gujarati Vowel Sign Voca..Gujarati Vowel Sign Voca
    (0x0b01u, 0x0b01u)  // ||iya Sign Candrabindu  ..||iya Sign Candrabindu
    (0x0b3cu, 0x0b3cu)  // ||iya Sign Nukta        ..||iya Sign Nukta
    (0x0b3fu, 0x0b3fu)  // ||iya Vowel Sign I      ..||iya Vowel Sign I
    (0x0b41u, 0x0b44u)  // ||iya Vowel Sign U      ..||iya Vowel Sign Vocalic
    (0x0b4du, 0x0b4du)  // ||iya Sign Virama       ..||iya Sign Virama
    (0x0b56u, 0x0b56u)  // ||iya Ai Length Mark    ..||iya Ai Length Mark
    (0x0b62u, 0x0b63u)  // ||iya Vowel Sign Vocalic..||iya Vowel Sign Vocalic
    (0x0b82u, 0x0b82u)  // Tamil Sign Anusvara     ..Tamil Sign Anusvara
    (0x0bc0u, 0x0bc0u)  // Tamil Vowel Sign Ii     ..Tamil Vowel Sign Ii
    (0x0bcdu, 0x0bcdu)  // Tamil Sign Virama       ..Tamil Sign Virama
    (0x0c00u, 0x0c00u)  // Telugu Sign Combining Ca..Telugu Sign Combining Ca
    (0x0c3eu, 0x0c40u)  // Telugu Vowel Sign Aa    ..Telugu Vowel Sign Ii
    (0x0c46u, 0x0c48u)  // Telugu Vowel Sign E     ..Telugu Vowel Sign Ai
    (0x0c4au, 0x0c4du)  // Telugu Vowel Sign O     ..Telugu Sign Virama
    (0x0c55u, 0x0c56u)  // Telugu Length Mark      ..Telugu Ai Length Mark
    (0x0c62u, 0x0c63u)  // Telugu Vowel Sign Vocali..Telugu Vowel Sign Vocali
    (0x0c81u, 0x0c81u)  // Kannada Sign Candrabindu..Kannada Sign Candrabindu
    (0x0cbcu, 0x0cbcu)  // Kannada Sign Nukta      ..Kannada Sign Nukta
    (0x0cbfu, 0x0cbfu)  // Kannada Vowel Sign I    ..Kannada Vowel Sign I
    (0x0cc6u, 0x0cc6u)  // Kannada Vowel Sign E    ..Kannada Vowel Sign E
    (0x0cccu, 0x0ccdu)  // Kannada Vowel Sign Au   ..Kannada Sign Virama
    (0x0ce2u, 0x0ce3u)  // Kannada Vowel Sign Vocal..Kannada Vowel Sign Vocal
    (0x0d01u, 0x0d01u)  // Malayalam Sign Candrabin..Malayalam Sign Candrabin
    (0x0d41u, 0x0d44u)  // Malayalam Vowel Sign U  ..Malayalam Vowel Sign Voc
    (0x0d4du, 0x0d4du)  // Malayalam Sign Virama   ..Malayalam Sign Virama
    (0x0d62u, 0x0d63u)  // Malayalam Vowel Sign Voc..Malayalam Vowel Sign Voc
    (0x0dcau, 0x0dcau)  // Sinhala Sign Al-lakuna  ..Sinhala Sign Al-lakuna
    (0x0dd2u, 0x0dd4u)  // Sinhala Vowel Sign Ketti..Sinhala Vowel Sign Ketti
    (0x0dd6u, 0x0dd6u)  // Sinhala Vowel Sign Diga ..Sinhala Vowel Sign Diga
    (0x0e31u, 0x0e31u)  // Thai Character Mai Han-a..Thai Character Mai Han-a
    (0x0e34u, 0x0e3au)  // Thai Character Sara I   ..Thai Character Phinthu
    (0x0e47u, 0x0e4eu)  // Thai Character Maitaikhu..Thai Character Yamakkan
    (0x0eb1u, 0x0eb1u)  // Lao Vowel Sign Mai Kan  ..Lao Vowel Sign Mai Kan
    (0x0eb4u, 0x0eb9u)  // Lao Vowel Sign I        ..Lao Vowel Sign Uu
    (0x0ebbu, 0x0ebcu)  // Lao Vowel Sign Mai Kon  ..Lao Semivowel Sign Lo
    (0x0ec8u, 0x0ecdu)  // Lao Tone Mai Ek         ..Lao Niggahita
    (0x0f18u, 0x0f19u)  // Tibetan Astrological Sig..Tibetan Astrological Sig
    (0x0f35u, 0x0f35u)  // Tibetan Mark Ngas Bzung ..Tibetan Mark Ngas Bzung
    (0x0f37u, 0x0f37u)  // Tibetan Mark Ngas Bzung ..Tibetan Mark Ngas Bzung
    (0x0f39u, 0x0f39u)  // Tibetan Mark Tsa -phru  ..Tibetan Mark Tsa -phru
    (0x0f71u, 0x0f7eu)  // Tibetan Vowel Sign Aa   ..Tibetan Sign Rjes Su Nga
    (0x0f80u, 0x0f84u)  // Tibetan Vowel Sign Rever..Tibetan Mark Halanta
    (0x0f86u, 0x0f87u)  // Tibetan Sign Lci Rtags  ..Tibetan Sign Yang Rtags
    (0x0f8du, 0x0f97u)  // Tibetan Subjoined Sign L..Tibetan Subjoined Letter
    (0x0f99u, 0x0fbcu)  // Tibetan Subjoined Letter..Tibetan Subjoined Letter
    (0x0fc6u, 0x0fc6u)  // Tibetan Symbol Padma Gda..Tibetan Symbol Padma Gda
    (0x102du, 0x1030u)  // Myanmar Vowel Sign I    ..Myanmar Vowel Sign Uu
    (0x1032u, 0x1037u)  // Myanmar Vowel Sign Ai   ..Myanmar Sign Dot Below
    (0x1039u, 0x103au)  // Myanmar Sign Virama     ..Myanmar Sign Asat
    (0x103du, 0x103eu)  // Myanmar Consonant Sign M..Myanmar Consonant Sign M
    (0x1058u, 0x1059u)  // Myanmar Vowel Sign Vocal..Myanmar Vowel Sign Vocal
    (0x105eu, 0x1060u)  // Myanmar Consonant Sign M..Myanmar Consonant Sign M
    (0x1071u, 0x1074u)  // Myanmar Vowel Sign Geba ..Myanmar Vowel Sign Kayah
    (0x1082u, 0x1082u)  // Myanmar Consonant Sign S..Myanmar Consonant Sign S
    (0x1085u, 0x1086u)  // Myanmar Vowel Sign Shan ..Myanmar Vowel Sign Shan
    (0x108du, 0x108du)  // Myanmar Sign Shan Counci..Myanmar Sign Shan Counci
    (0x109du, 0x109du)  // Myanmar Vowel Sign Aiton..Myanmar Vowel Sign Aiton
    (0x135du, 0x135fu)  // Ethiopic Combining Gemin..Ethiopic Combining Gemin
    (0x1712u, 0x1714u)  // Tagalog Vowel Sign I    ..Tagalog Sign Virama
    (0x1732u, 0x1734u)  // Hanunoo Vowel Sign I    ..Hanunoo Sign Pamudpod
    (0x1752u, 0x1753u)  // Buhid Vowel Sign I      ..Buhid Vowel Sign U
    (0x1772u, 0x1773u)  // Tagbanwa Vowel Sign I   ..Tagbanwa Vowel Sign U
    (0x17b4u, 0x17b5u)  // Khmer Vowel Inherent Aq ..Khmer Vowel Inherent Aa
    (0x17b7u, 0x17bdu)  // Khmer Vowel Sign I      ..Khmer Vowel Sign Ua
    (0x17c6u, 0x17c6u)  // Khmer Sign Nikahit      ..Khmer Sign Nikahit
    (0x17c9u, 0x17d3u)  // Khmer Sign Muusikatoan  ..Khmer Sign Bathamasat
    (0x17ddu, 0x17ddu)  // Khmer Sign Atthacan     ..Khmer Sign Atthacan
    (0x180bu, 0x180du)  // Mongolian Free Variation..Mongolian Free Variation
    (0x1885u, 0x1886u)  // Mongolian Letter Ali Gal..Mongolian Letter Ali Gal
    (0x18a9u, 0x18a9u)  // Mongolian Letter Ali Gal..Mongolian Letter Ali Gal
    (0x1920u, 0x1922u)  // Limbu Vowel Sign A      ..Limbu Vowel Sign U
    (0x1927u, 0x1928u)  // Limbu Vowel Sign E      ..Limbu Vowel Sign O
    (0x1932u, 0x1932u)  // Limbu Small Letter Anusv..Limbu Small Letter Anusv
    (0x1939u, 0x193bu)  // Limbu Sign Mukphreng    ..Limbu Sign Sa-i
    (0x1a17u, 0x1a18u)  // Buginese Vowel Sign I   ..Buginese Vowel Sign U
    (0x1a1bu, 0x1a1bu)  // Buginese Vowel Sign Ae  ..Buginese Vowel Sign Ae
    (0x1a56u, 0x1a56u)  // Tai Tham Consonant Sign ..Tai Tham Consonant Sign
    (0x1a58u, 0x1a5eu)  // Tai Tham Sign Mai Kang L..Tai Tham Consonant Sign
    (0x1a60u, 0x1a60u)  // Tai Tham Sign Sakot     ..Tai Tham Sign Sakot
    (0x1a62u, 0x1a62u)  // Tai Tham Vowel Sign Mai ..Tai Tham Vowel Sign Mai
    (0x1a65u, 0x1a6cu)  // Tai Tham Vowel Sign I   ..Tai Tham Vowel Sign Oa B
    (0x1a73u, 0x1a7cu)  // Tai Tham Vowel Sign Oa A..Tai Tham Sign Khuen-lue
    (0x1a7fu, 0x1a7fu)  // Tai Tham Combining Crypt..Tai Tham Combining Crypt
    (0x1ab0u, 0x1abeu)  // Combining Doubled Circum..Combining Parentheses Ov
    (0x1b00u, 0x1b03u)  // Balinese Sign Ulu Ricem ..Balinese Sign Surang
    (0x1b34u, 0x1b34u)  // Balinese Sign Rerekan   ..Balinese Sign Rerekan
    (0x1b36u, 0x1b3au)  // Balinese Vowel Sign Ulu ..Balinese Vowel Sign Ra R
    (0x1b3cu, 0x1b3cu)  // Balinese Vowel Sign La L..Balinese Vowel Sign La L
    (0x1b42u, 0x1b42u)  // Balinese Vowel Sign Pepe..Balinese Vowel Sign Pepe
    (0x1b6bu, 0x1b73u)  // Balinese Musical Symbol ..Balinese Musical Symbol
    (0x1b80u, 0x1b81u)  // Sundanese Sign Panyecek ..Sundanese Sign Panglayar
    (0x1ba2u, 0x1ba5u)  // Sundanese Consonant Sign..Sundanese Vowel Sign Pan
    (0x1ba8u, 0x1ba9u)  // Sundanese Vowel Sign Pam..Sundanese Vowel Sign Pan
    (0x1babu, 0x1badu)  // Sundanese Sign Virama   ..Sundanese Consonant Sign
    (0x1be6u, 0x1be6u)  // Batak Sign Tompi        ..Batak Sign Tompi
    (0x1be8u, 0x1be9u)  // Batak Vowel Sign Pakpak ..Batak Vowel Sign Ee
    (0x1bedu, 0x1bedu)  // Batak Vowel Sign Karo O ..Batak Vowel Sign Karo O
    (0x1befu, 0x1bf1u)  // Batak Vowel Sign U F|| S..Batak Consonant Sign H
    (0x1c2cu, 0x1c33u)  // Lepcha Vowel Sign E     ..Lepcha Consonant Sign T
    (0x1c36u, 0x1c37u)  // Lepcha Sign Ran         ..Lepcha Sign Nukta
    (0x1cd0u, 0x1cd2u)  // Vedic Tone Karshana     ..Vedic Tone Prenkha
    (0x1cd4u, 0x1ce0u)  // Vedic Sign Yajurvedic Mi..Vedic Tone Rigvedic Kash
    (0x1ce2u, 0x1ce8u)  // Vedic Sign Visarga Svari..Vedic Sign Visarga Anuda
    (0x1cedu, 0x1cedu)  // Vedic Sign Tiryak       ..Vedic Sign Tiryak
    (0x1cf4u, 0x1cf4u)  // Vedic Tone Candra Above ..Vedic Tone Candra Above
    (0x1cf8u, 0x1cf9u)  // Vedic Tone Ring Above   ..Vedic Tone Double Ring A
    (0x1dc0u, 0x1df5u)  // Combining Dotted Grave A..Combining Up Tack Above
    (0x1dfbu, 0x1dffu)  // (nil)                   ..Combining Right Arrowhea
    (0x20d0u, 0x20f0u)  // Combining Left Harpoon A..Combining Asterisk Above
    (0x2cefu, 0x2cf1u)  // Coptic Combining Ni Abov..Coptic Combining Spiritu
    (0x2d7fu, 0x2d7fu)  // Tifinagh Consonant Joine..Tifinagh Consonant Joine
    (0x2de0u, 0x2dffu)  // Combining Cyrillic Lette..Combining Cyrillic Lette
    (0x302au, 0x302du)  // Ideographic Level Tone M..Ideographic Entering Ton
    (0x3099u, 0x309au)  // Combining Katakana-hirag..Combining Katakana-hirag
    (0xa66fu, 0xa672u)  // Combining Cyrillic Vzmet..Combining Cyrillic Thous
    (0xa674u, 0xa67du)  // Combining Cyrillic Lette..Combining Cyrillic Payer
    (0xa69eu, 0xa69fu)  // Combining Cyrillic Lette..Combining Cyrillic Lette
    (0xa6f0u, 0xa6f1u)  // Bamum Combining Mark Koq..Bamum Combining Mark Tuk
    (0xa802u, 0xa802u)  // Syloti Nagri Sign Dvisva..Syloti Nagri Sign Dvisva
    (0xa806u, 0xa806u)  // Syloti Nagri Sign Hasant..Syloti Nagri Sign Hasant
    (0xa80bu, 0xa80bu)  // Syloti Nagri Sign Anusva..Syloti Nagri Sign Anusva
    (0xa825u, 0xa826u)  // Syloti Nagri Vowel Sign ..Syloti Nagri Vowel Sign
    (0xa8c4u, 0xa8c5u)  // Saurashtra Sign Virama  ..
    (0xa8e0u, 0xa8f1u)  // Combining Devanagari Dig..Combining Devanagari Sig
    (0xa926u, 0xa92du)  // Kayah Li Vowel Ue       ..Kayah Li Tone Calya Plop
    (0xa947u, 0xa951u)  // Rejang Vowel Sign I     ..Rejang Consonant Sign R
    (0xa980u, 0xa982u)  // Javanese Sign Panyangga ..Javanese Sign Layar
    (0xa9b3u, 0xa9b3u)  // Javanese Sign Cecak Telu..Javanese Sign Cecak Telu
    (0xa9b6u, 0xa9b9u)  // Javanese Vowel Sign Wulu..Javanese Vowel Sign Suku
    (0xa9bcu, 0xa9bcu)  // Javanese Vowel Sign Pepe..Javanese Vowel Sign Pepe
    (0xa9e5u, 0xa9e5u)  // Myanmar Sign Shan Saw   ..Myanmar Sign Shan Saw
    (0xaa29u, 0xaa2eu)  // Cham Vowel Sign Aa      ..Cham Vowel Sign Oe
    (0xaa31u, 0xaa32u)  // Cham Vowel Sign Au      ..Cham Vowel Sign Ue
    (0xaa35u, 0xaa36u)  // Cham Consonant Sign La  ..Cham Consonant Sign Wa
    (0xaa43u, 0xaa43u)  // Cham Consonant Sign Fina..Cham Consonant Sign Fina
    (0xaa4cu, 0xaa4cu)  // Cham Consonant Sign Fina..Cham Consonant Sign Fina
    (0xaa7cu, 0xaa7cu)  // Myanmar Sign Tai Laing T..Myanmar Sign Tai Laing T
    (0xaab0u, 0xaab0u)  // Tai Viet Mai Kang       ..Tai Viet Mai Kang
    (0xaab2u, 0xaab4u)  // Tai Viet Vowel I        ..Tai Viet Vowel U
    (0xaab7u, 0xaab8u)  // Tai Viet Mai Khit       ..Tai Viet Vowel Ia
    (0xaabeu, 0xaabfu)  // Tai Viet Vowel Am       ..Tai Viet Tone Mai Ek
    (0xaac1u, 0xaac1u)  // Tai Viet Tone Mai Tho   ..Tai Viet Tone Mai Tho
    (0xaaecu, 0xaaedu)  // Meetei Mayek Vowel Sign ..Meetei Mayek Vowel Sign
    (0xaaf6u, 0xaaf6u)  // Meetei Mayek Virama     ..Meetei Mayek Virama
    (0xabe5u, 0xabe5u)  // Meetei Mayek Vowel Sign ..Meetei Mayek Vowel Sign
    (0xabe8u, 0xabe8u)  // Meetei Mayek Vowel Sign ..Meetei Mayek Vowel Sign
    (0xabedu, 0xabedu)  // Meetei Mayek Apun Iyek  ..Meetei Mayek Apun Iyek
    (0xfb1eu, 0xfb1eu)  // Hebrew Point Judeo-spani..Hebrew Point Judeo-spani
    (0xfe00u, 0xfe0fu)  // Variation Select||-1    ..Variation Select||-16
    (0xfe20u, 0xfe2fu)  // Combining Ligature Left ..Combining Cyrillic Titlo
    (0x101fdu, 0x101fdu)  // Phaistos Disc Sign Combi..Phaistos Disc Sign Combi
    (0x102e0u, 0x102e0u)  // Coptic Epact Thousands M..Coptic Epact Thousands M
    (0x10376u, 0x1037au)  // Combining Old Permic Let..Combining Old Permic Let
    (0x10a01u, 0x10a03u)  // Kharoshthi Vowel Sign I ..Kharoshthi Vowel Sign Vo
    (0x10a05u, 0x10a06u)  // Kharoshthi Vowel Sign E ..Kharoshthi Vowel Sign O
    (0x10a0cu, 0x10a0fu)  // Kharoshthi Vowel Length ..Kharoshthi Sign Visarga
    (0x10a38u, 0x10a3au)  // Kharoshthi Sign Bar Abov..Kharoshthi Sign Dot Belo
    (0x10a3fu, 0x10a3fu)  // Kharoshthi Virama       ..Kharoshthi Virama
    (0x10ae5u, 0x10ae6u)  // Manichaean Abbreviation ..Manichaean Abbreviation
    (0x11001u, 0x11001u)  // Brahmi Sign Anusvara    ..Brahmi Sign Anusvara
    (0x11038u, 0x11046u)  // Brahmi Vowel Sign Aa    ..Brahmi Virama
    (0x1107fu, 0x11081u)  // Brahmi Number Joiner    ..Kaithi Sign Anusvara
    (0x110b3u, 0x110b6u)  // Kaithi Vowel Sign U     ..Kaithi Vowel Sign Ai
    (0x110b9u, 0x110bau)  // Kaithi Sign Virama      ..Kaithi Sign Nukta
    (0x11100u, 0x11102u)  // Chakma Sign Candrabindu ..Chakma Sign Visarga
    (0x11127u, 0x1112bu)  // Chakma Vowel Sign A     ..Chakma Vowel Sign Uu
    (0x1112du, 0x11134u)  // Chakma Vowel Sign Ai    ..Chakma Maayyaa
    (0x11173u, 0x11173u)  // Mahajani Sign Nukta     ..Mahajani Sign Nukta
    (0x11180u, 0x11181u)  // Sharada Sign Candrabindu..Sharada Sign Anusvara
    (0x111b6u, 0x111beu)  // Sharada Vowel Sign U    ..Sharada Vowel Sign O
    (0x111cau, 0x111ccu)  // Sharada Sign Nukta      ..Sharada Extra Sh||t Vowe
    (0x1122fu, 0x11231u)  // Khojki Vowel Sign U     ..Khojki Vowel Sign Ai
    (0x11234u, 0x11234u)  // Khojki Sign Anusvara    ..Khojki Sign Anusvara
    (0x11236u, 0x11237u)  // Khojki Sign Nukta       ..Khojki Sign Shadda
    (0x1123eu, 0x1123eu)  // (nil)                   ..
    (0x112dfu, 0x112dfu)  // Khudawadi Sign Anusvara ..Khudawadi Sign Anusvara
    (0x112e3u, 0x112eau)  // Khudawadi Vowel Sign U  ..Khudawadi Sign Virama
    (0x11300u, 0x11301u)  // Grantha Sign Combining A..Grantha Sign Candrabindu
    (0x1133cu, 0x1133cu)  // Grantha Sign Nukta      ..Grantha Sign Nukta
    (0x11340u, 0x11340u)  // Grantha Vowel Sign Ii   ..Grantha Vowel Sign Ii
    (0x11366u, 0x1136cu)  // Combining Grantha Digit ..Combining Grantha Digit
    (0x11370u, 0x11374u)  // Combining Grantha Letter..Combining Grantha Letter
    (0x11438u, 0x1143fu)  // (nil)                   ..
    (0x11442u, 0x11444u)  // (nil)                   ..
    (0x11446u, 0x11446u)  // (nil)                   ..
    (0x114b3u, 0x114b8u)  // Tirhuta Vowel Sign U    ..Tirhuta Vowel Sign Vocal
    (0x114bau, 0x114bau)  // Tirhuta Vowel Sign Sh||t..Tirhuta Vowel Sign Sh||t
    (0x114bfu, 0x114c0u)  // Tirhuta Sign Candrabindu..Tirhuta Sign Anusvara
    (0x114c2u, 0x114c3u)  // Tirhuta Sign Virama     ..Tirhuta Sign Nukta
    (0x115b2u, 0x115b5u)  // Siddham Vowel Sign U    ..Siddham Vowel Sign Vocal
    (0x115bcu, 0x115bdu)  // Siddham Sign Candrabindu..Siddham Sign Anusvara
    (0x115bfu, 0x115c0u)  // Siddham Sign Virama     ..Siddham Sign Nukta
    (0x115dcu, 0x115ddu)  // Siddham Vowel Sign Alter..Siddham Vowel Sign Alter
    (0x11633u, 0x1163au)  // Modi Vowel Sign U       ..Modi Vowel Sign Ai
    (0x1163du, 0x1163du)  // Modi Sign Anusvara      ..Modi Sign Anusvara
    (0x1163fu, 0x11640u)  // Modi Sign Virama        ..Modi Sign Ardhacandra
    (0x116abu, 0x116abu)  // Takri Sign Anusvara     ..Takri Sign Anusvara
    (0x116adu, 0x116adu)  // Takri Vowel Sign Aa     ..Takri Vowel Sign Aa
    (0x116b0u, 0x116b5u)  // Takri Vowel Sign U      ..Takri Vowel Sign Au
    (0x116b7u, 0x116b7u)  // Takri Sign Nukta        ..Takri Sign Nukta
    (0x1171du, 0x1171fu)  // Ahom Consonant Sign Medi..Ahom Consonant Sign Medi
    (0x11722u, 0x11725u)  // Ahom Vowel Sign I       ..Ahom Vowel Sign Uu
    (0x11727u, 0x1172bu)  // Ahom Vowel Sign Aw      ..Ahom Sign Killer
    (0x11c30u, 0x11c36u)  // (nil)                   ..
    (0x11c38u, 0x11c3du)  // (nil)                   ..
    (0x11c3fu, 0x11c3fu)  // (nil)                   ..
    (0x11c92u, 0x11ca7u)  // (nil)                   ..
    (0x11caau, 0x11cb0u)  // (nil)                   ..
    (0x11cb2u, 0x11cb3u)  // (nil)                   ..
    (0x11cb5u, 0x11cb6u)  // (nil)                   ..
    (0x16af0u, 0x16af4u)  // Bassa Vah Combining High..Bassa Vah Combining High
    (0x16b30u, 0x16b36u)  // Pahawh Hmong Mark Cim Tu..Pahawh Hmong Mark Cim Ta
    (0x16f8fu, 0x16f92u)  // Miao Tone Right         ..Miao Tone Below
    (0x1bc9du, 0x1bc9eu)  // Duployan Thick Letter Se..Duployan Double Mark
    (0x1d167u, 0x1d169u)  // Musical Symbol Combining..Musical Symbol Combining
    (0x1d17bu, 0x1d182u)  // Musical Symbol Combining..Musical Symbol Combining
    (0x1d185u, 0x1d18bu)  // Musical Symbol Combining..Musical Symbol Combining
    (0x1d1aau, 0x1d1adu)  // Musical Symbol Combining..Musical Symbol Combining
    (0x1d242u, 0x1d244u)  // Combining Greek Musical ..Combining Greek Musical
    (0x1da00u, 0x1da36u)  // Signwriting Head Rim    ..Signwriting Air Sucking
    (0x1da3bu, 0x1da6cu)  // Signwriting Mouth Closed..Signwriting Excitement
    (0x1da75u, 0x1da75u)  // Signwriting Upper Body T..Signwriting Upper Body T
    (0x1da84u, 0x1da84u)  // Signwriting Location Hea..Signwriting Location Hea
    (0x1da9bu, 0x1da9fu)  // Signwriting Fill Modifie..Signwriting Fill Modifie
    (0x1daa1u, 0x1daafu)  // Signwriting Rotation Mod..Signwriting Rotation Mod
    (0x1e000u, 0x1e006u)  // (nil)                   ..
    (0x1e008u, 0x1e018u)  // (nil)                   ..
    (0x1e01bu, 0x1e021u)  // (nil)                   ..
    (0x1e023u, 0x1e024u)  // (nil)                   ..
    (0x1e026u, 0x1e02au)  // (nil)                   ..
    (0x1e8d0u, 0x1e8d6u)  // Mende Kikakui Combining ..Mende Kikakui Combining
    (0x1e944u, 0x1e94au)  // (nil)                   ..
    (0xe0100u, 0xe01efu)  // Variation Select||-17   ..Variation Select||-256
|]

// from: http://www.unicode.org/emoji/charts-12.0/emoji-list.html
// see emoji.tsv, the processed table ordered by codepoint
// ref: https://unicode.org/reports/tr51/

let Emoji = [|
    ( 0x0023u,  0x0023u)  // keycap: #               ..keycap: #               
    ( 0x002Au,  0x002Au)  // keycap: *               ..keycap: *               
    ( 0x0030u,  0x0039u)  // keycap: 0               ..keycap: 9               
    ( 0x00A9u,  0x00A9u)  // copyright               ..copyright               
    ( 0x00AEu,  0x00AEu)  // registered              ..registered              
    ( 0x203Cu,  0x203Cu)  // double exclamation mark ..double exclamation mark 
    ( 0x2049u,  0x2049u)  // exclamation question mark..exclamation question mark
    ( 0x2122u,  0x2122u)  // trade mark              ..trade mark              
    ( 0x2139u,  0x2139u)  // information             ..information             
    ( 0x2194u,  0x2199u)  // left-right arrow        ..down-left arrow         
    ( 0x21A9u,  0x21AAu)  // right arrow curving left..left arrow curving right
    ( 0x231Au,  0x231Bu)  // watch                   ..hourglass done          
    ( 0x2328u,  0x2328u)  // keyboard                ..keyboard                
    ( 0x23CFu,  0x23CFu)  // eject button            ..eject button            
    ( 0x23E9u,  0x23F3u)  // fast-forward button     ..hourglass not done      
    ( 0x23F8u,  0x23FAu)  // pause button            ..record button           
    ( 0x24C2u,  0x24C2u)  // circled M               ..circled M               
    ( 0x25AAu,  0x25ABu)  // black small square      ..white small square      
    ( 0x25B6u,  0x25B6u)  // play button             ..play button             
    ( 0x25C0u,  0x25C0u)  // reverse button          ..reverse button          
    ( 0x25FBu,  0x25FEu)  // white medium square     ..black medium-small square
    ( 0x2600u,  0x2604u)  // sun                     ..comet                   
    ( 0x260Eu,  0x260Eu)  // telephone               ..telephone               
    ( 0x2611u,  0x2611u)  // check box with check    ..check box with check    
    ( 0x2614u,  0x2615u)  // umbrella with rain drops..hot beverage            
    ( 0x2618u,  0x2618u)  // shamrock                ..shamrock                
    ( 0x261Du,  0x261Du)  // index pointing up       ..index pointing up       
    ( 0x2620u,  0x2620u)  // skull and crossbones    ..skull and crossbones    
    ( 0x2622u,  0x2623u)  // radioactive             ..biohazard               
    ( 0x2626u,  0x2626u)  // orthodox cross          ..orthodox cross          
    ( 0x262Au,  0x262Au)  // star and crescent       ..star and crescent       
    ( 0x262Eu,  0x262Fu)  // peace symbol            ..yin yang                
    ( 0x2638u,  0x263Au)  // wheel of dharma         ..smiling face            
    ( 0x2640u,  0x2640u)  // female sign             ..female sign             
    ( 0x2642u,  0x2642u)  // male sign               ..male sign               
    ( 0x2648u,  0x2653u)  // Aries                   ..Pisces                  
    ( 0x265Fu,  0x2660u)  // chess pawn              ..spade suit              
    ( 0x2663u,  0x2663u)  // club suit               ..club suit               
    ( 0x2665u,  0x2666u)  // heart suit              ..diamond suit            
    ( 0x2668u,  0x2668u)  // hot springs             ..hot springs             
    ( 0x267Bu,  0x267Bu)  // recycling symbol        ..recycling symbol        
    ( 0x267Eu,  0x267Fu)  // infinity                ..wheelchair symbol       
    ( 0x2692u,  0x2697u)  // hammer and pick         ..alembic                 
    ( 0x2699u,  0x2699u)  // gear                    ..gear                    
    ( 0x269Bu,  0x269Cu)  // atom symbol             ..fleur-de-lis            
    ( 0x26A0u,  0x26A1u)  // warning                 ..high voltage            
    ( 0x26AAu,  0x26ABu)  // white circle            ..black circle            
    ( 0x26B0u,  0x26B1u)  // coffin                  ..funeral urn             
    ( 0x26BDu,  0x26BEu)  // soccer ball             ..baseball                
    ( 0x26C4u,  0x26C5u)  // snowman without snow    ..sun behind cloud        
    ( 0x26C8u,  0x26C8u)  // cloud with lightning and rain..cloud with lightning and rain
    ( 0x26CEu,  0x26CFu)  // Ophiuchus               ..pick                    
    ( 0x26D1u,  0x26D1u)  // rescue worker’s helmet  ..rescue worker’s helmet  
    ( 0x26D3u,  0x26D4u)  // chains                  ..no entry                
    ( 0x26E9u,  0x26EAu)  // shinto shrine           ..church                  
    ( 0x26F0u,  0x26F5u)  // mountain                ..sailboat                
    ( 0x26F7u,  0x26FAu)  // skier                   ..tent                    
    ( 0x26FDu,  0x26FDu)  // fuel pump               ..fuel pump               
    ( 0x2702u,  0x2702u)  // scissors                ..scissors                
    ( 0x2705u,  0x2705u)  // check mark button       ..check mark button       
    ( 0x2708u,  0x270Du)  // airplane                ..writing hand            
    ( 0x270Fu,  0x270Fu)  // pencil                  ..pencil                  
    ( 0x2712u,  0x2712u)  // black nib               ..black nib               
    ( 0x2714u,  0x2714u)  // check mark              ..check mark              
    ( 0x2716u,  0x2716u)  // multiplication sign     ..multiplication sign     
    ( 0x271Du,  0x271Du)  // latin cross             ..latin cross             
    ( 0x2721u,  0x2721u)  // star of David           ..star of David           
    ( 0x2728u,  0x2728u)  // sparkles                ..sparkles                
    ( 0x2733u,  0x2734u)  // eight-spoked asterisk   ..eight-pointed star      
    ( 0x2744u,  0x2744u)  // snowflake               ..snowflake               
    ( 0x2747u,  0x2747u)  // sparkle                 ..sparkle                 
    ( 0x274Cu,  0x274Cu)  // cross mark              ..cross mark              
    ( 0x274Eu,  0x274Eu)  // cross mark button       ..cross mark button       
    ( 0x2753u,  0x2755u)  // question mark           ..white exclamation mark  
    ( 0x2757u,  0x2757u)  // exclamation mark        ..exclamation mark        
    ( 0x2763u,  0x2764u)  // heart exclamation       ..red heart               
    ( 0x2795u,  0x2797u)  // plus sign               ..division sign           
    ( 0x27A1u,  0x27A1u)  // right arrow             ..right arrow             
    ( 0x27B0u,  0x27B0u)  // curly loop              ..curly loop              
    ( 0x27BFu,  0x27BFu)  // double curly loop       ..double curly loop       
    ( 0x2934u,  0x2935u)  // right arrow curving up  ..right arrow curving down
    ( 0x2B05u,  0x2B07u)  // left arrow              ..down arrow              
    ( 0x2B1Bu,  0x2B1Cu)  // black large square      ..white large square      
    ( 0x2B50u,  0x2B50u)  // star                    ..star                    
    ( 0x2B55u,  0x2B55u)  // hollow red circle       ..hollow red circle       
    ( 0x3030u,  0x3030u)  // wavy dash               ..wavy dash               
    ( 0x303Du,  0x303Du)  // part alternation mark   ..part alternation mark   
    ( 0x3297u,  0x3297u)  // Japanese “congratulations” button..Japanese “congratulations” button
    ( 0x3299u,  0x3299u)  // Japanese “secret” button..Japanese “secret” button
    (0x1F004u, 0x1F004u)  // mahjong red dragon      ..mahjong red dragon      
    (0x1F0CFu, 0x1F0CFu)  // joker                   ..joker                   
    (0x1F170u, 0x1F171u)  // A button (blood type)   ..B button (blood type)   
    (0x1F17Eu, 0x1F17Fu)  // O button (blood type)   ..P button                
    (0x1F18Eu, 0x1F18Eu)  // AB button (blood type)  ..AB button (blood type)  
    (0x1F191u, 0x1F19Au)  // CL button               ..VS button               
    (0x1F1E6u, 0x1F1FFu)  // flag: Ascension Island  ..flag: Zimbabwe          
    (0x1F201u, 0x1F202u)  // Japanese “here” button  ..Japanese “service charge” button
    (0x1F21Au, 0x1F21Au)  // Japanese “free of charge” button..Japanese “free of charge” button
    (0x1F22Fu, 0x1F22Fu)  // Japanese “reserved” button..Japanese “reserved” button
    (0x1F232u, 0x1F23Au)  // Japanese “prohibited” button..Japanese “open for business” button
    (0x1F250u, 0x1F251u)  // Japanese “bargain” button..Japanese “acceptable” button
    (0x1F300u, 0x1F321u)  // cyclone                 ..thermometer             
    (0x1F324u, 0x1F393u)  // sun behind small cloud  ..graduation cap          
    (0x1F396u, 0x1F397u)  // military medal          ..reminder ribbon         
    (0x1F399u, 0x1F39Bu)  // studio microphone       ..control knobs           
    (0x1F39Eu, 0x1F3F0u)  // film frames             ..castle                  
    (0x1F3F3u, 0x1F3F5u)  // white flag              ..rosette                 
    (0x1F3F7u, 0x1F3FAu)  // label                   ..amphora                 
    (0x1F400u, 0x1F4FDu)  // rat                     ..film projector          
    (0x1F4FFu, 0x1F53Du)  // prayer beads            ..downwards button        
    (0x1F549u, 0x1F54Eu)  // om                      ..menorah                 
    (0x1F550u, 0x1F567u)  // one o’clock             ..twelve-thirty           
    (0x1F56Fu, 0x1F570u)  // candle                  ..mantelpiece clock       
    (0x1F573u, 0x1F57Au)  // hole                    ..man dancing             
    (0x1F587u, 0x1F587u)  // linked paperclips       ..linked paperclips       
    (0x1F58Au, 0x1F58Du)  // pen                     ..crayon                  
    (0x1F590u, 0x1F590u)  // hand with fingers splayed..hand with fingers splayed
    (0x1F595u, 0x1F596u)  // middle finger           ..vulcan salute           
    (0x1F5A4u, 0x1F5A5u)  // black heart             ..desktop computer        
    (0x1F5A8u, 0x1F5A8u)  // printer                 ..printer                 
    (0x1F5B1u, 0x1F5B2u)  // computer mouse          ..trackball               
    (0x1F5BCu, 0x1F5BCu)  // framed picture          ..framed picture          
    (0x1F5C2u, 0x1F5C4u)  // card index dividers     ..file cabinet            
    (0x1F5D1u, 0x1F5D3u)  // wastebasket             ..spiral calendar         
    (0x1F5DCu, 0x1F5DEu)  // clamp                   ..rolled-up newspaper     
    (0x1F5E1u, 0x1F5E1u)  // dagger                  ..dagger                  
    (0x1F5E3u, 0x1F5E3u)  // speaking head           ..speaking head           
    (0x1F5E8u, 0x1F5E8u)  // left speech bubble      ..left speech bubble      
    (0x1F5EFu, 0x1F5EFu)  // right anger bubble      ..right anger bubble      
    (0x1F5F3u, 0x1F5F3u)  // ballot box with ballot  ..ballot box with ballot  
    (0x1F5FAu, 0x1F64Fu)  // world map               ..folded hands            
    (0x1F680u, 0x1F6C5u)  // rocket                  ..left luggage            
    (0x1F6CBu, 0x1F6D2u)  // couch and lamp          ..shopping cart           
    (0x1F6D5u, 0x1F6D5u)  // ⊛ hindu temple          ..⊛ hindu temple          
    (0x1F6E0u, 0x1F6E5u)  // hammer and wrench       ..motor boat              
    (0x1F6E9u, 0x1F6E9u)  // small airplane          ..small airplane          
    (0x1F6EBu, 0x1F6ECu)  // airplane departure      ..airplane arrival        
    (0x1F6F0u, 0x1F6F0u)  // satellite               ..satellite               
    (0x1F6F3u, 0x1F6FAu)  // passenger ship          ..⊛ auto rickshaw         
    (0x1F7E0u, 0x1F7EBu)  // ⊛ orange circle         ..⊛ brown square          
    (0x1F90Du, 0x1F93Au)  // ⊛ white heart           ..person fencing          
    (0x1F93Cu, 0x1F945u)  // people wrestling        ..goal net                
    (0x1F947u, 0x1F971u)  // 1st place medal         ..⊛ yawning face          
    (0x1F973u, 0x1F976u)  // partying face           ..cold face               
    (0x1F97Au, 0x1F9A2u)  // pleading face           ..swan                    
    (0x1F9A5u, 0x1F9AAu)  // ⊛ sloth                 ..⊛ oyster                
    (0x1F9AEu, 0x1F9CAu)  // ⊛ guide dog             ..⊛ ice                   
    (0x1F9CDu, 0x1F9FFu)  // ⊛ person standing       ..nazar amulet            
    (0x1FA70u, 0x1FA73u)  // ⊛ ballet shoes          ..⊛ shorts                
    (0x1FA78u, 0x1FA7Au)  // ⊛ drop of blood         ..⊛ stethoscope           
    (0x1FA80u, 0x1FA82u)  // ⊛ yo-yo                 ..⊛ parachute      
|]

// https://github.com/jquast/wcwidth/blob/master/wcwidth/table_wide.py
// at commit 0d7de112202cc8b2ebe9232ff4a5c954f19d561a (2016-07-02):
let WideEastAsian = [|
    ( 0x1100u,  0x115fu)  // Hangul Choseong Kiyeok  ..Hangul Choseong Filler
    ( 0x2329u,  0x232au)  // Left-pointing Angle Brac..Right-pointing Angle Bra
    ( 0x2e80u,  0x2e99u)  // Cjk Radical Repeat      ..Cjk Radical Rap
    ( 0x2e9bu,  0x2ef3u)  // Cjk Radical Choke       ..Cjk Radical C-simplified
    ( 0x2f00u,  0x2fd5u)  // Kangxi Radical One      ..Kangxi Radical Flute
    ( 0x2ff0u,  0x2ffbu)  // Ideographic Description ..Ideographic Description
    ( 0x3000u,  0x303eu)  // Ideographic Space       ..Ideographic Variation In
    ( 0x3041u,  0x3096u)  // Hiragana Letter Small A ..Hiragana Letter Small Ke
    ( 0x3099u,  0x30ffu)  // Combining Katakana-hirag..Katakana Digraph Koto
    ( 0x3105u,  0x312du)  // Bopomofo Letter B       ..Bopomofo Letter Ih
    ( 0x3131u,  0x318eu)  // Hangul Letter Kiyeok    ..Hangul Letter Araeae
    ( 0x3190u,  0x31bau)  // Ideographic Annotation L..Bopomofo Letter Zy
    ( 0x31c0u,  0x31e3u)  // Cjk Stroke T            ..Cjk Stroke Q
    ( 0x31f0u,  0x321eu)  // Katakana Letter Small Ku..Parenthesized K||ean Cha
    ( 0x3220u,  0x3247u)  // Parenthesized Ideograph ..Circled Ideograph Koto
    ( 0x3250u,  0x32feu)  // Partnership Sign        ..Circled Katakana Wo
    ( 0x3300u,  0x4dbfu)  // Square Apaato           ..
    ( 0x4e00u,  0xa48cu)  // Cjk Unified Ideograph-4e..Yi Syllable Yyr
    ( 0xa490u,  0xa4c6u)  // Yi Radical Qot          ..Yi Radical Ke
    ( 0xa960u,  0xa97cu)  // Hangul Choseong Tikeut-m..Hangul Choseong Ssangyeo
    ( 0xac00u,  0xd7a3u)  // Hangul Syllable Ga      ..Hangul Syllable Hih
    ( 0xf900u,  0xfaffu)  // Cjk Compatibility Ideogr..
    ( 0xfe10u,  0xfe19u)  // Presentation F||m F|| Ve..Presentation F||m F|| Ve
    ( 0xfe30u,  0xfe52u)  // Presentation F||m F|| Ve..Small Full Stop
    ( 0xfe54u,  0xfe66u)  // Small Semicolon         ..Small Equals Sign
    ( 0xfe68u,  0xfe6bu)  // Small Reverse Solidus   ..Small Commercial At
    ( 0xff01u,  0xff60u)  // Fullwidth Exclamation Ma..Fullwidth Right White Pa
    ( 0xffe0u,  0xffe6u)  // Fullwidth Cent Sign     ..Fullwidth Won Sign
    (0x20000u, 0x2fffdu)  // Cjk Unified Ideograph-20..
    (0x30000u, 0x3fffdu)  // (nil)                   ..
|]

let Powerline = [|
    (0xE0A0u, 0xE0A3u)
    (0xE0B0u, 0xE0C8u)
    (0xE0CAu, 0xE0CAu)
    (0xE0CCu, 0xE0D4u)
|]

// The complete set is recorded in nerdfont.txt
let NerdFont = [|
    (0x23FBu, 0x23FEu)  // Power Symbols
    (0x2665u, 0x2665u)  // Octicons
    (0x26A1u, 0x26A1u)  // Octicons
    (0x2B58u, 0x2B58u)  // Power Symbols
    (0xE000u, 0xE00Au)  // Pomicons
    (0xE200u, 0xE2A9u)  // Font Awesome Extension
    (0xE300u, 0xE3EBu)  // Weather Icons
    (0xE5FAu, 0xE631u)  // Seti-UI + Custom
    (0xE700u, 0xE7C5u)  // Devicons
    (0xEA60u, 0xEBEBu)  // Codicons
    (0xF000u, 0xF2E0u)  // Font Awesome
    (0xF300u, 0xF32Du)  // Font Logos (Font Linux)
    (0xF400u, 0xF4A9u)  // Octicons
    (0xF500u, 0xFD46u)  // Material
|]

let private intable (table: (uint*uint)[]) (ucs: uint) =
    let rec intable_impl lower upper =
        if lower > upper then false
        else
        let mid = (lower + upper) / 2
        let p1, p2 = table.[mid]
        if p1 <= ucs && ucs <= p2 then true
        elif ucs < p1 then intable_impl lower (mid-1) 
        else intable_impl (mid+1) upper
    intable_impl 0 (table.Length - 1)

type CharType =
| Control     = -1
| Invisible   = 0
| Narrow      = 1
| Powerline   = 2
| Wide        = 3
| Nerd        = 4
| Emoji       = 5
| Braille     = 6

let private _wcwidth_cache: hashmap<uint, CharType> = hashmap []

let private _wcwidth_impl =
    function
    // NOTE: created by hand, there isn't anything identifiable other than
    // general Cf category code to identify these, and some characters in Cf
    // category code are of non-zero width.
    | 0x0000u | 0x034Fu | 0x2028u | 0x2029u  -> CharType.Invisible
    | x when    0x200Bu <= x && x <= 0x200Fu
             || 0x202Au <= x && x <= 0x202Eu
             || 0x2060u <= x && x <= 0x2063u -> CharType.Invisible
    // C0/C1 control characters.
    | x when                    x < 0x0020u
             || 0x007Fu <= x && x < 0x00A0u  -> CharType.Control
    // neovim uses these in drawing the UI
    | 0x2502u | 0x2630u | 0x2026u            -> CharType.Narrow
    // ASCII-7
    | x when                    x < 0x007Fu  -> CharType.Narrow
    | x when intable Emoji x                 -> CharType.Emoji
    | x when intable Powerline x             -> CharType.Powerline
    | x when intable NerdFont x              -> CharType.Nerd
    | x when intable WideEastAsian x         -> CharType.Wide
    // Combining characters with zero width.
    | x when intable ZeroWidth x             -> CharType.Invisible
    // Braille patterns
    | x when    0x2800u <= x && x <= 0x28FFu -> CharType.Braille
    | ucs ->
    #if DEBUG
        trace "wcwidth" "unknown codepoint: %c (%X)" (char ucs) (ucs)
    #endif
        CharType.Narrow

let wcwidth(ucs: uint) =
    match _wcwidth_cache.TryGetValue ucs with
    | true, ty -> ty
    | _ ->
    let ty = _wcwidth_impl ucs
    _wcwidth_cache.[ucs] <- ty
    ty

let wswidth (x: Rune) = 
  match x with
  | SingleChar _
  | SurrogatePair _ -> wcwidth x.Codepoint
  | Composed xs -> 
    match wcwidth xs.[0].Codepoint with
    | CharType.Emoji -> CharType.Emoji
    | _ -> CharType.Wide

/// <summary>
/// true if the string could be a part of a programming
/// symbol ligature.
/// </summary>
let isProgrammingSymbol = 
    function
    | SingleChar c1 when System.Char.IsWhiteSpace c1 -> false 
    // disable the frequent symbols that's too expensive to draw
    | SingleChar('\'' | '"' | '{' | '}') -> false
    | SingleChar chr -> System.Char.IsSymbol chr || System.Char.IsPunctuation chr
    | _ -> false

let CharTypeWidth(x: CharType): int =
    match x with
    | CharType.Control | CharType.Invisible -> 0
    | CharType.Wide | CharType.Nerd | CharType.Emoji -> 2
    | _ -> 1

