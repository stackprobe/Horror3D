namespace HorrorGame;

/// <summary>目撃者エンドの時間とページ進行。NEXT以外で本文を読み飛ばさない。</summary>
internal sealed class WitnessEnding
{
	public static readonly string[][] Pages =
	[
		[
			"あなたは森を脱出した。",
			"館の一家も、最後の目撃者も、もうこの世にはいない。",
			"誰も追ってこなかった。",
			"誰にも罪を問われなかった。",
			"あなたは元の生活に戻った。",
			"しかし、数日後。",
			"街角に、見覚えのある人影が立っていた。",
			"あの森で撃ち殺した男だった。"
		],
		[
			"男は何も言わなかった。",
			"近づいてくることもなかった。",
			"ただ、じっとあなたを見つめていた。",
			"周囲の人々は、誰も男に気づかない。",
			"あなたが指をさしても、そこには誰もいないと言う。",
			"それから、男はどこにでも現れるようになった。",
			"通勤電車の向かい側。",
			"食事をするレストランの隅。",
			"夜中に目を覚ましたときの、寝室の扉の前。"
		],
		[
			"男は決して襲ってこない。",
			"何も要求しない。",
			"恨み言ひとつ口にしない。",
			"ただ、あなたを見ている。",
			"あなたが何をしたのか、すべて知っている。",
			"そして、そのことを知っているのは、もうあなただけだった。",
			"あなたは目撃者を殺した。",
			"しかし、目撃者を消すことはできなかった。"
		]
	];
	public const float FadeSeconds = 1.6f, TextFadeSeconds = 1.2f, DarknessSeconds = .8f;
	public const float EyeFadeSeconds = 1.5f, EyeHoldSeconds = 3, EyeOutSeconds = 1;
	public const float SmileDelaySeconds = .6f, SmileFadeSeconds = 1.2f;
	public float FadeTime { get; private set; }
	public float FinaleTime { get; private set; }
	public int Page { get; private set; }
	public bool Finale { get; private set; }
	public bool Reading => FadeTime >= FadeSeconds && !Finale;
	public float WorldFade => Math.Clamp(FadeTime / FadeSeconds, 0, 1);
	public float TextOpacity => Finale ? Math.Clamp(1 - FinaleTime / TextFadeSeconds, 0, 1) : 1;
	public float EyeOpacity
	{
		get
		{
			float time = FinaleTime - TextFadeSeconds - DarknessSeconds;
			return Math.Clamp(time / EyeFadeSeconds, 0, 1) *
				Math.Clamp((EyeFadeSeconds + EyeHoldSeconds + EyeOutSeconds - time) / EyeOutSeconds, 0, 1);
		}
	}
	public float SmileProgress => Math.Clamp(
		(FinaleTime - TextFadeSeconds - DarknessSeconds - EyeFadeSeconds - SmileDelaySeconds) / SmileFadeSeconds, 0, 1);
	public float SmileOpacity => SmileProgress * EyeOpacity;
	public bool Complete => Finale && FinaleTime >= TextFadeSeconds + DarknessSeconds + EyeFadeSeconds + EyeHoldSeconds + EyeOutSeconds;
	public void Update(float dt)
	{
		if (Finale) FinaleTime += dt;
		else FadeTime = Math.Min(FadeSeconds, FadeTime + dt);
	}
	public void Next()
	{
		if (!Reading) return;
		if (Page < Pages.Length - 1) Page++;
		else Finale = true;
	}
}
