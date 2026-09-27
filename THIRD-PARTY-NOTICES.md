# Third-party notices

Radio Passthrough is licensed under the GNU General Public License v3.0 (see `LICENSE`). It includes or is derived from the following.

## ACRE2 radio effect

`src/RadioPassthrough.Core/Dsp/AcreRadioEffect.cs` reimplements the receive-side radio effect of ACRE2 (`CFilterRadio`, `AcreDsp`, `PinkNoise`). It uses the same processing order and constants so the test preview sounds like the real thing.

- Project: https://github.com/IDI-Systems/acre2
- License: GNU General Public License v3.0

## NAudio

Windows audio capture and playback (WASAPI).

- Copyright © Mark Heath
- License: MIT. https://github.com/naudio/NAudio

> Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED.

## Concentus (Opus codec)

Used only by the Test tab, to hear audio the way TeamSpeak's Opus codec sends it.

```
Copyright (c) by various holding parties, including (but not limited to):
Skype Limited, Xiph.Org Foundation, CSIRO, Microsoft Corporation,
Jean-Marc Valin, Gregory Maxwell, Mark Borgerding, Timothy B. Terriberry,
Logan Stromberg. All rights are reserved by their respective holders.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met:

* Redistributions of source code must retain the above copyright notice, this
  list of conditions and the following disclaimer.

* Redistributions in binary form must reproduce the above copyright notice,
  this list of conditions and the following disclaimer in the documentation
  and/or other materials provided with the distribution.

* Neither the name of Internet Society, IETF or IETF Trust, nor the
   names of specific contributors, may be used to endorse or promote
   products derived from this software without specific prior written
   permission.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS"
AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE
IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT HOLDER OR CONTRIBUTORS BE LIABLE
FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL
DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER
CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY,
OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
```

## .NET

The app ships with the .NET runtime (self-contained). Copyright © .NET Foundation and Contributors. MIT license. https://github.com/dotnet/runtime

## VB-CABLE (not included)

VB-CABLE is **not** bundled or downloaded by the app. *Get VB-CABLE* on the Setup tab opens VB-Audio's website, and you install it yourself. VB-CABLE is donationware by VB-Audio Software (https://vb-audio.com/Cable/). Its license and any donation are between you and VB-Audio.
