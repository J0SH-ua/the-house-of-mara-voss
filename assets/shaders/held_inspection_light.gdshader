shader_type canvas_item;

// Local-only lighting for Mara's inspection hand and the item she holds.
// The room is never sampled or modified: the real beam's screen-space
// position simply lets each sprite pixel sit in light, penumbra, or shadow.
uniform bool darkness_active = false;
uniform bool beam_active = false;
uniform vec2 beam_center_uv = vec2(0.5, 0.5);
uniform vec2 viewport_size = vec2(1152.0, 648.0);
uniform float beam_radius = 158.0;
uniform float penumbra_width = 58.0;
uniform float shadow_brightness = 0.06;
uniform float penumbra_brightness = 0.36;

void fragment() {
    // Fragment COLOR already includes the default texture and modulation.
    // Keep the original hand/item colors unchanged in the lit part of the beam.
    vec4 sprite = COLOR;
    float brightness = 1.0;

    if (darkness_active) {
        brightness = shadow_brightness;
        if (beam_active) {
            float distance_to_beam = length((SCREEN_UV - beam_center_uv) * viewport_size);
            float in_beam = 1.0 - smoothstep(beam_radius - 3.0, beam_radius + 3.0, distance_to_beam);
            float in_penumbra = 1.0 - smoothstep(
                beam_radius + penumbra_width - 3.0,
                beam_radius + penumbra_width + 3.0,
                distance_to_beam);
            brightness = mix(shadow_brightness, penumbra_brightness, in_penumbra);
            brightness = mix(brightness, 1.0, in_beam);
        }
    }

    COLOR = vec4(sprite.rgb * brightness, sprite.a);
}
