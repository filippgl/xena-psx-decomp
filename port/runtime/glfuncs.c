/* OpenGL function loading and shader helpers. */
#include "glfuncs.h"
#include "hw.h"

#define GL_DEFINE(ret, name, args) ret(APIENTRY* p_##name) args;
GL_FUNCS(GL_DEFINE)
#undef GL_DEFINE

int gl_load(void) {
    int ok = 1;
#define GL_LOAD(ret, name, args) \
    p_##name = (ret(APIENTRY*) args)SDL_GL_GetProcAddress(#name); \
    if (!p_##name) { log_msg("OpenGL: missing %s", #name); ok = 0; }
    GL_FUNCS(GL_LOAD)
#undef GL_LOAD
    return ok;
}

static GLuint compile(GLenum type, const char* src) {
    GLuint s = p_glCreateShader(type);
    p_glShaderSource(s, 1, &src, NULL);
    p_glCompileShader(s);
    GLint ok = 0;
    p_glGetShaderiv(s, GL_COMPILE_STATUS, &ok);
    if (!ok) {
        char log[4096];
        p_glGetShaderInfoLog(s, sizeof(log), NULL, log);
        log_msg("OpenGL shader error:\n%s", log);
        return 0;
    }
    return s;
}

GLuint gl_program(const char* vs, const char* fs) {
    GLuint v = compile(GL_VERTEX_SHADER, vs), f = compile(GL_FRAGMENT_SHADER, fs);
    if (!v || !f) return 0;
    GLuint p = p_glCreateProgram();
    p_glAttachShader(p, v);
    p_glAttachShader(p, f);
    p_glBindFragDataLocation(p, 0, "o_color");
    static const char* attrs[] = {"a_pos", "a_uvq", "a_col", "a_p", "a_c"};
    for (int i = 0; i < 5; i++) p_glBindAttribLocation(p, (GLuint)i, attrs[i]);
    p_glLinkProgram(p);
    GLint ok = 0;
    p_glGetProgramiv(p, GL_LINK_STATUS, &ok);
    if (!ok) {
        char log[4096];
        p_glGetProgramInfoLog(p, sizeof(log), NULL, log);
        log_msg("OpenGL link error:\n%s", log);
        return 0;
    }
    return p;
}
