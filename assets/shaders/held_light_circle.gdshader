shader_type canvas_item;
render_mode unshaded;

uniform vec4 dim_color : source_color = vec4(0.005, 0.005, 0.008, 0.88);
uniform bool beam_enabled = false;
uniform vec2 beam_center_px = vec2(576.0, 324.0);
uniform vec2 viewport_size_px = vec2(1152.0, 648.0);
uniform float beam_radius_px = 152.0;
uniform float beam_softness_px = 18.0;
uniform vec4 beam_light_color : source_color = vec4(1.0, 0.92, 0.78, 0.12);

void fragment() {
    float beam_strength = 0.0;
    if (beam_enabled) {
        // The held dimmer is a full-viewport ColorRect, so local UV maps
        // directly to viewport pixels and lines up with the real room beam.
        vec2 pixel_position = UV * viewport_size_px;
        float distance_to_beam = distance(pixel_position, beam_center_px);
        beam_strength = 1.0 - smoothstep(
            beam_radius_px - beam_softness_px,
            beam_radius_px + beam_softness_px,
            distance_to_beam);
    }

    vec4 incoming_modulate = COLOR;
    vec3 overlay_rgb = mix(dim_color.rgb, beam_light_color.rgb, beam_strength);
    float overlay_alpha = mix(dim_color.a, beam_light_color.a, beam_strength);
    COLOR = vec4(overlay_rgb * incoming_modulate.rgb,
        overlay_alpha * incoming_modulate.a);
}
