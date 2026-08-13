# Cool-Mod-For-CloneHero
This mod adds some cool features I would like Clone Hero to have.

## How to install
Just replace the **Assembly-CSharp.dll** file in the path (I recommend making a copy first, in case something goes wrong):

\AppData\Local\Programs\Clone Hero\Clone Hero_Data\Managed\Assembly-CSharp.dll

Now in the \Clone Hero folder where you also put songs in, not the AppData one:
Put the **yourock.opus** file in (create the folder if it doesn't exist yet):

\Clone Hero\Custom\Sounds\

I also provide a \Menu Backgrounds folder with a few backgrounds, if you want to add them.

## Features

 - **Custom menu backgrounds**:

Easily add them to

*\Clone Hero\Custom\Menu Backgrounds*

and they'll be available in the game menu settings.

 - **Menu background slideshow:**

Settings > Video > Menu BG Slideshow

If set to 'Yes' the game will start changing the menu background every 15 minutes (by default) going through your **\Custom\Menu Backgrounds** folder.

You can set the time between each backgorund change in the **\Clone Hero\settings.ini** file:
Under **[video]** just change the **menu_bg_slideshow_seconds** parameter to the time in seconds of your preference.

 - **New 'Difficulty' 0-100 scale info of the song:**

Instead of just the arbitrarily set variable 'Intensity', now we show under the right panel of the song details the **Difficulty** variable, mathematically calculated to represent the general difficulty of the song, it goes from 0-100. the formula is calculated like this:

**NPS per chart** : 10-second peak , using a sliding window aligned with note timings.

**Weighted values:** 0.25 average across instruments + 0.75 maximum.

**Reference calibrated to 14:** 14 NPS as the maximum.

You can generate every song Difficulty by selecting:

Settings > General > Calculate Difficulty

it's the last option of the list. The new Sort Option **'Difficulty'** will appear.

 - **Favorite songs:**

There's an **Add to Favorites** / **Remove from Favorites** option for every song in the **Song Options** menu and a **Favorites** filter in the **Filter Options**.

 - **Custom sound at the end of the song:**

The game plays the classic **'You Rock'** sound (from GH3) at the end of the song. You can change it by replacing the file **\Clone Hero\Custom\Sounds\yourock.opus**.
