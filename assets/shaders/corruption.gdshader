shader_type canvas_item;

// Memory-corruption screen effect.
// intensity 0 = the house as Mara insists it is; 1 = barely holding together.
// Driven by GameUI from GameState.Corruption01; `frozen` honours Reduce Motion.

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear;

uniform float intensity : hint_range(0.0, 1.0) = 0.0;
uniform float frozen : hint_range(0.0, 1.0) = 0.0; // 1 = stop all temporal motion

float hash12(vec2 p) {
	return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

void fragment() {
	float t = TIME * 60.0;
	float frame = mix(floor(t), 7.0, frozen);

	vec2 uv = SCREEN_UV;
	vec2 centered = uv - vec2(0.5);
	float dist = length(centered);

	// Occasional horizontal tear, stronger toward the shattering point.
	float band = step(0.985 - intensity * 0.05, hash12(vec2(floor(uv.y * 48.0), frame)));
	float tear = band * (hash12(vec2(frame, uv.y)) - 0.5) * 0.012 * intensity;
	uv.x += tear;

	// Chromatic aberration growing toward the edges.
	float ca = intensity * intensity * dist * 0.012;
	vec3 col;
	col.r = texture(screen_texture, uv + centered * ca).r;
	col.g = texture(screen_texture, uv).g;
	col.b = texture(screen_texture, uv - centered * ca).b;

	// Cold desaturation: the warmth is the first thing misremembered.
	float lum = dot(col, vec3(0.299, 0.587, 0.114));
	col = mix(col, vec3(lum), intensity * 0.28);
	col *= mix(vec3(1.0), vec3(0.94, 0.97, 1.05), intensity);

	// Film grain.
	float grain = hash12(uv * (311.0 + mod(frame, 13.0))) - 0.5;
	col += grain * (0.015 + intensity * 0.075);

	// Breathing vignette closing in.
	float vig = smoothstep(0.95 - intensity * 0.32, 0.35, dist);
	col *= mix(1.0, vig, 0.25 + intensity * 0.55);

	COLOR = vec4(col, 1.0);
}
